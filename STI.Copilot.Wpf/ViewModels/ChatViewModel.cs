using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using STI.Copilot.Core.Chat;
using STI.Copilot.Core.Documents;
using STI.Copilot.Core.Ollama;
using STI.Copilot.Core.Rag;
using STI.Copilot.Core.Runbook;
using STI.Copilot.Wpf.Mvvm;

namespace STI.Copilot.Wpf.ViewModels
{
    /// <summary>
    /// 채팅 + RAG 화면의 ViewModel. 코어(OllamaClient/ChatService/RAG)를 감싸 UI에 바인딩한다.
    /// PDF/DOCX 지원 여부는 생성자에 주입하는 DocumentExtractorRegistry 로 결정된다
    /// (텍스트만 필요하면 기본값, PDF 필요하면 호스트가 추출기를 등록해 넘김).
    /// </summary>
    public class ChatViewModel : ViewModelBase
    {
        private readonly OllamaClient _client;
        private readonly OllamaOptions _opts;   // 런타임 토글(GPU 사용 등)용 — 요청마다 실시간 반영됨
        private readonly ChatService _chat;
        private readonly RagContextProvider _rag;
        private readonly RunbookContextProvider _runbookProvider;   // null 가능(런북 서비스 미주입 시)
        private readonly CompositeContextProvider _composite;
        private readonly InMemoryVectorStore _store;
        private readonly IngestionService _ingestion;
        private readonly Dispatcher _dispatcher;
        private readonly string _embeddingModel;

        private string _inputText = string.Empty;
        private string _selectedModel;
        private string _statusText = "준비됨";
        private bool _isBusy;
        private bool _useDocuments;   // 기본 OFF: 자유 대화. 체크하면 문서 근거 기반.
        private bool _useRunbook;     // 기본 OFF: 체크하면 런북 근거 기반.
        private bool _useGpu;         // 기본 OFF: 채팅 생성 CPU. 체크하면 GPU 사용(테스트용).
        private string _ingestStatus = string.Empty;
        private double _ingestProgress;
        private bool _isIngesting;
        private bool _isSidebarExpanded = true;
        private CancellationTokenSource _sendCts;    // 답변 생성 취소용
        private CancellationTokenSource _ingestCts;  // 인덱싱 취소용
        private string _selectedScope = AllDocumentsLabel;
        private readonly ExtractionOptions _extractionOptions;

        public ChatViewModel(
            OllamaOptions options = null,
            string systemPrompt = null,
            DocumentExtractorRegistry extractors = null,
            RagOptions ragOptions = null,
            ExtractionOptions extractionOptions = null,
            RunbookService runbookService = null)
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            var opts = options ?? new OllamaOptions();
            _opts = opts;
            _useGpu = opts.ChatNumGpu != 0; // 0=CPU→false, null(auto)/그 외→true(GPU)
            var rag = ragOptions ?? new RagOptions();
            var reg = extractors ?? DocumentExtractorRegistry.CreateDefault();
            _extractionOptions = extractionOptions ?? new ExtractionOptions();

            _client = new OllamaClient(opts);
            _chat = new ChatService(_client, systemPrompt);
            _selectedModel = opts.ChatModel;
            _embeddingModel = opts.EmbeddingModel; // 대화 모델 목록에서 제외할 임베딩 모델

            _store = new InMemoryVectorStore(rag.StorePath, opts.EmbeddingModel);
            _store.Load();
            _rag = new RagContextProvider(_client, _store, rag);
            _ingestion = new IngestionService(_client, reg, _store);

            // 런북 근거 제공자(서비스가 주입된 경우) + 런북·문서를 묶는 복합 제공자.
            _runbookProvider = runbookService != null ? new RunbookContextProvider(runbookService) : null;
            _composite = new CompositeContextProvider(_runbookProvider, _rag);
            UpdateContextProvider(); // 기본값(둘 다 OFF) → 순수 채팅

            Messages = new ObservableCollection<ChatMessageViewModel>();
            Models = new ObservableCollection<string>();
            Documents = new ObservableCollection<string>();
            DocumentScopes = new ObservableCollection<string> { AllDocumentsLabel };

            SendCommand = new AsyncRelayCommand(SendAsync, () => !_isBusy && !string.IsNullOrWhiteSpace(InputText));
            ClearCommand = new RelayCommand(ClearConversation, () => !_isBusy);
            LoadDocumentsCommand = new AsyncRelayCommand(LoadDocumentsAsync, () => !_isBusy && !_isIngesting);
            ClearKnowledgeBaseCommand = new RelayCommand(ClearKnowledgeBase, () => !_isBusy && !_isIngesting);
            ToggleSidebarCommand = new RelayCommand(() => IsSidebarExpanded = !IsSidebarExpanded);
            StopCommand = new RelayCommand(() => _sendCts?.Cancel(), () => _isBusy);
            CancelIngestCommand = new RelayCommand(() => _ingestCts?.Cancel(), () => _isIngesting);

            RefreshDocuments();
            if (_store.NeedsReindex)
                _statusText = "임베딩 모델이 변경되어 문서를 다시 등록해야 합니다.";
        }

        public ObservableCollection<ChatMessageViewModel> Messages { get; }
        public ObservableCollection<string> Models { get; }
        public ObservableCollection<string> Documents { get; }

        /// <summary>"전체 문서"를 뜻하는 항목 라벨.</summary>
        public const string AllDocumentsLabel = "전체 문서";

        /// <summary>검색 범위 선택 목록("전체 문서" + 등록된 각 문서).</summary>
        public ObservableCollection<string> DocumentScopes { get; }

        /// <summary>현재 선택된 검색 범위. "전체 문서"면 전체, 특정 파일명이면 그 문서만 검색.</summary>
        public string SelectedScope
        {
            get => _selectedScope;
            set
            {
                if (SetProperty(ref _selectedScope, value))
                    _rag.DocumentFilter =
                        (string.IsNullOrEmpty(value) || value == AllDocumentsLabel) ? null : value;
            }
        }

        public AsyncRelayCommand SendCommand { get; }
        public RelayCommand ClearCommand { get; }
        public AsyncRelayCommand LoadDocumentsCommand { get; }
        public RelayCommand ClearKnowledgeBaseCommand { get; }
        public RelayCommand ToggleSidebarCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand CancelIngestCommand { get; }

        /// <summary>사이드바 펼침 여부. 접으면 슬림 레일만 표시된다.</summary>
        public bool IsSidebarExpanded
        {
            get => _isSidebarExpanded;
            set => SetProperty(ref _isSidebarExpanded, value);
        }

        public string InputText
        {
            get => _inputText;
            set { if (SetProperty(ref _inputText, value)) SendCommand.RaiseCanExecuteChanged(); }
        }

        public string SelectedModel
        {
            get => _selectedModel;
            set { if (SetProperty(ref _selectedModel, value)) _chat.Model = value; }
        }

        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        /// <summary>문서 기반 답변(RAG) 사용 여부. 런북과 함께 체크 가능.</summary>
        public bool UseDocuments
        {
            get => _useDocuments;
            set { if (SetProperty(ref _useDocuments, value)) UpdateContextProvider(); }
        }

        /// <summary>런북 기반 답변 사용 여부. 체크하면 등록된 런북에서만 근거를 찾는다.</summary>
        public bool UseRunbook
        {
            get => _useRunbook;
            set { if (SetProperty(ref _useRunbook, value)) UpdateContextProvider(); }
        }

        /// <summary>채팅 생성에 GPU 사용 여부(테스트용). 체크=GPU 자동, 해제=CPU 강제.
        /// 임베딩(bge-m3)은 구형 GPU 크래시 때문에 항상 CPU로 둔다.
        /// _opts 를 바꾸면 다음 요청부터 즉시 반영된다.</summary>
        public bool UseGpu
        {
            get => _useGpu;
            set
            {
                if (!SetProperty(ref _useGpu, value)) return;
                // 정석: GPU=auto(null → Ollama가 VRAM 보고 최적 레이어 오프로딩) / CPU=0(강제).
                _opts.ChatNumGpu = value ? (int?)null : 0;
                // 전환이 즉시 반영되도록 현재 모델을 내린다 → 다음 질문에서 새 모드로 재로딩.
                // (안 그러면 Ollama가 이미 로드된 인스턴스를 재사용해 전환이 안 먹는다.)
                _ = _client.UnloadAsync(SelectedModel);
            }
        }

        // 체크 상태에 따라 대화 근거 제공자를 정한다.
        // 둘 다 해제 → null(자유 대화). 하나 이상 체크 → 복합 제공자(엄격 근거 답변).
        private void UpdateContextProvider()
        {
            if (!_useRunbook && !_useDocuments)
            {
                _chat.ContextProvider = null;
                return;
            }
            _composite.IncludeRunbook = _useRunbook && _runbookProvider != null;
            _composite.IncludeDocuments = _useDocuments;
            _chat.ContextProvider = _composite;
        }

        /// <summary>PDF OCR(스캔 문서 글자 인식) 사용 여부. 켜면 느리지만 이미지 PDF도 인식. 다음 인덱싱부터 적용.</summary>
        public bool UseOcr
        {
            get => _extractionOptions.UseOcr;
            set
            {
                if (_extractionOptions.UseOcr != value)
                {
                    _extractionOptions.UseOcr = value;
                    OnPropertyChanged();
                }
            }
        }

        public string IngestStatus { get => _ingestStatus; set => SetProperty(ref _ingestStatus, value); }
        public double IngestProgress { get => _ingestProgress; set => SetProperty(ref _ingestProgress, value); }
        public bool IsIngesting
        {
            get => _isIngesting;
            private set
            {
                if (SetProperty(ref _isIngesting, value))
                {
                    LoadDocumentsCommand.RaiseCanExecuteChanged();
                    ClearKnowledgeBaseCommand.RaiseCanExecuteChanged();
                    CancelIngestCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>직전 답변에 사용된 근거 출처 목록. 각 항목이 칩 하나로 표시된다.</summary>
        public ObservableCollection<SourceChip> LastSources { get; } = new ObservableCollection<SourceChip>();

        /// <summary>출처가 하나라도 있으면 true(출처 영역 표시 여부).</summary>
        public bool HasSources => LastSources.Count > 0;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    SendCommand.RaiseCanExecuteChanged();
                    ClearCommand.RaiseCanExecuteChanged();
                    LoadDocumentsCommand.RaiseCanExecuteChanged();
                    StopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>설치된 모델 목록을 로드한다. 뷰의 Loaded 시점에 호출.</summary>
        public async Task LoadModelsAsync()
        {
            try
            {
                StatusText = "모델 목록 불러오는 중...";
                var models = await _client.ListModelsAsync();
                Models.Clear();
                // 임베딩 전용 모델(bge-m3 등)은 대화 생성을 못 하므로 목록에서 제외한다.
                foreach (var m in models)
                    if (!IsEmbeddingModel(m)) Models.Add(m);

                if (!string.IsNullOrEmpty(_selectedModel) && Models.Contains(_selectedModel))
                    SelectedModel = _selectedModel;
                else if (Models.Count > 0)
                    SelectedModel = Models[0];

                StatusText = Models.Count > 0 ? "준비됨" : "대화 가능한 모델이 없습니다.";
            }
            catch (Exception ex)
            {
                StatusText = "Ollama 연결 실패: " + ex.Message;
            }
        }

        // 임베딩 전용 모델인지 판별(대화 모델 목록에서 제외용).
        // 설정된 임베딩 모델과 태그를 무시하고 비교하고, 대표적인 임베딩 모델 이름도 방어적으로 걸러낸다.
        private bool IsEmbeddingModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return false;
            var name = BaseName(modelName);

            if (string.Equals(name, BaseName(_embeddingModel), StringComparison.OrdinalIgnoreCase))
                return true;

            string[] embedHints = { "bge-", "nomic-embed", "all-minilm", "mxbai-embed", "snowflake-arctic-embed", "-embed" };
            foreach (var h in embedHints)
                if (name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        // 모델 이름에서 태그(':' 뒤)를 제외한 기본 이름. "bge-m3:latest" → "bge-m3"
        private static string BaseName(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return "";
            int i = modelName.IndexOf(':');
            return i >= 0 ? modelName.Substring(0, i) : modelName;
        }

        private async Task SendAsync()
        {
            var userText = InputText.Trim();
            if (string.IsNullOrEmpty(userText)) return;

            IsBusy = true;
            StatusText = "생성 중...";
            ClearSources();
            InputText = string.Empty;

            Messages.Add(new ChatMessageViewModel(ChatRole.User, userText));
            var assistant = new ChatMessageViewModel(ChatRole.Assistant, string.Empty) { IsGenerating = true };
            Messages.Add(assistant);

            _sendCts = new CancellationTokenSource();
            try
            {
                _chat.Model = SelectedModel;
                await _chat.AskAsync(userText,
                    token => _dispatcher.Invoke(() => assistant.Content += token),
                    _sendCts.Token,
                    onContextReady: () => _dispatcher.Invoke(() =>
                    {
                        // 런북 근거가 있으면 "전체 목록"을 먼저 즉시 출력하고, 이어서 상세를 스트리밍한다.
                        if (_useRunbook && _runbookProvider != null &&
                            !string.IsNullOrEmpty(_runbookProvider.LastListText))
                            assistant.Content = _runbookProvider.LastListText;
                    }));

                // 근거(런북·문서) 기반이면 실제 사용된 출처 표시
                UpdateSources();
                StatusText = "준비됨";
            }
            catch (OperationCanceledException)
            {
                // 사용자가 생성을 중지함. 지금까지 받은 부분 답변은 그대로 두고 표시만 남긴다.
                assistant.Content += string.IsNullOrEmpty(assistant.Content) ? "(중지됨)" : "  …(중지됨)";
                StatusText = "중지됨";
            }
            catch (Exception ex)
            {
                assistant.Content = "[오류] " + ex.Message;
                StatusText = "오류 발생";
            }
            finally
            {
                assistant.IsGenerating = false;
                _sendCts?.Dispose();
                _sendCts = null;
                IsBusy = false;
            }
        }

        // 직전 답변에 실제 사용된 근거 출처를 "참고 · 런북 rb-3(제목) · 문서 파일명" 형태로 만든다.
        // 직전 답변에 실제 사용된 근거를 출처 칩 목록으로 갱신한다(런북 항목별 1개, 문서별 1개).
        private void UpdateSources()
        {
            LastSources.Clear();
            if (_useRunbook && _runbookProvider != null)
            {
                foreach (var h in _runbookProvider.LastHits)
                    LastSources.Add(new SourceChip("런북", h.Entry.Id + " · " + h.Entry.Title));
            }
            if (_useDocuments)
            {
                foreach (var doc in _rag.LastSources.Select(s => s.Chunk.DocumentId).Distinct())
                    LastSources.Add(new SourceChip("문서", doc));
            }
            OnPropertyChanged(nameof(HasSources));
        }

        private void ClearSources()
        {
            if (LastSources.Count == 0) return;
            LastSources.Clear();
            OnPropertyChanged(nameof(HasSources));
        }

        private async Task LoadDocumentsAsync()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "지식베이스에 추가할 문서 선택",
                Filter = "지원 문서|*.txt;*.md;*.csv;*.log;*.pdf;*.docx|모든 파일|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            await IngestFilesAsync(dlg.FileNames);
        }

        /// <summary>
        /// 주어진 파일들을 지식베이스에 등록한다. 파일 선택 대화상자와 드래그앤드롭이 공유한다.
        /// 지원하지 않는 파일은 건너뛴다.
        /// </summary>
        public async Task IngestFilesAsync(IEnumerable<string> paths)
        {
            if (paths == null || _isBusy || _isIngesting) return;

            _ingestCts = new CancellationTokenSource();
            IsIngesting = true;
            IngestProgress = 0;
            try
            {
                foreach (var path in paths)
                {
                    var name = System.IO.Path.GetFileName(path);
                    if (!_ingestion.CanHandle(path))
                    {
                        IngestStatus = $"건너뜀(미지원): {name}";
                        continue;
                    }
                    try
                    {
                        // 추출(표 인식 등)은 시간이 걸릴 수 있어, 임베딩 진행률이 오기 전 상태를 먼저 표시.
                        IngestStatus = $"{name} 분석 중…";
                        IngestProgress = 0;
                        await _ingestion.IngestAsync(path, (done, total) =>
                            _dispatcher.Invoke(() =>
                            {
                                IngestStatus = $"인덱싱 {name}: {done}/{total}";
                                IngestProgress = total > 0 ? (done * 100.0 / total) : 0;
                            }), _ingestCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // 취소는 바깥에서 처리(파일 실패로 오인해 계속 진행하지 않도록)
                    }
                    catch (Exception exFile)
                    {
                        IngestStatus = $"실패: {name} - {exFile.Message}";
                    }
                }
                RefreshDocuments();
                IngestStatus = $"문서 등록 완료 (총 {Documents.Count}개 문서)";
            }
            catch (OperationCanceledException)
            {
                RefreshDocuments();
                IngestStatus = "인덱싱 중지됨";
            }
            catch (Exception ex)
            {
                IngestStatus = "오류: " + ex.Message;
            }
            finally
            {
                _ingestCts?.Dispose();
                _ingestCts = null;
                IngestProgress = 0;
                IsIngesting = false;
            }
        }

        private void ClearKnowledgeBase()
        {
            _store.Clear();
            _store.Save();
            RefreshDocuments();
            IngestStatus = "지식베이스를 비웠습니다.";
        }

        private void RefreshDocuments()
        {
            Documents.Clear();
            foreach (var d in _store.DocumentIds) Documents.Add(d);

            // 검색 범위 목록도 갱신("전체 문서" + 각 문서). 기존 선택이 남아있으면 유지, 없으면 전체로.
            var prev = SelectedScope;
            DocumentScopes.Clear();
            DocumentScopes.Add(AllDocumentsLabel);
            foreach (var d in _store.DocumentIds) DocumentScopes.Add(d);
            SelectedScope = (prev != null && DocumentScopes.Contains(prev)) ? prev : AllDocumentsLabel;
        }

        private void ClearConversation()
        {
            _chat.Reset();
            Messages.Clear();
            ClearSources();
            StatusText = "대화를 초기화했습니다.";
        }
    }

    /// <summary>출처 영역에 표시되는 칩 한 개(런북/문서 구분 + 표시 텍스트).</summary>
    public class SourceChip
    {
        public SourceChip(string kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        /// <summary>"런북" 또는 "문서". 칩 색상 구분에 사용.</summary>
        public string Kind { get; }

        /// <summary>칩에 표시할 본문(런북: "id · 제목", 문서: 파일명).</summary>
        public string Text { get; }

        /// <summary>런북 출처면 true(문서와 색상 구분용 트리거).</summary>
        public bool IsRunbook => Kind == "런북";
    }
}
