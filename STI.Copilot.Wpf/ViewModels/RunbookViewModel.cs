using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using STI.Copilot.Core.Runbook;
using STI.Copilot.Wpf.Mvvm;

namespace STI.Copilot.Wpf.ViewModels
{
    /// <summary>
    /// RunBook 관리 화면 VM. RunbookService(코어)를 감싸 목록·키워드검색·필터를 UI에 바인딩한다.
    /// (3단계: 조회·검색·필터 + 탭 전환. 추가/수정/폐기/삭제 "동작"은 4~5단계에서 연결.)
    /// </summary>
    public class RunbookViewModel : ViewModelBase
    {
        private readonly RunbookService _service;
        private bool _loaded;

        private string _searchText = string.Empty;
        private string _selectedCategory;   // null = 전체
        private bool _showDeprecated;        // false = 활성만

        // 항목 추가/수정 폼 상태
        private bool _isAddOpen;
        private bool _isSaving;
        private string _editingId;   // null = 신규 추가, 값 있으면 수정 대상 id
        private string _newTitle = string.Empty;
        private string _newContent = string.Empty;
        private string _newCategory = RunbookCategory.Action;
        private string _newCode = string.Empty;    // 알람/코드 (선택)
        private string _newModule = string.Empty;  // 모듈/장치 ID (선택)
        private string _newTags = string.Empty;    // 태그 (쉼표 구분, 선택)

        // 대체(supersede) 제안: 입력 멈추면 유사 항목 자동 검색
        private readonly DispatcherTimer _similarTimer;
        private bool _isFindingSimilar;

        // 목록 검색 디바운스: 타이핑 중엔 목록 재생성을 미뤄 버벅임 방지
        private readonly DispatcherTimer _searchTimer;

        // 전체 재임베딩(관리자 유지보수)
        private bool _isReindexing;
        private double _reindexProgress;
        private string _reindexStatus = string.Empty;

        public RunbookViewModel(RunbookService service)
        {
            _service = service;
            Items = new ObservableCollection<RunbookEntry>();
            SimilarItems = new ObservableCollection<RunbookSimilarItem>();
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanSave);
            ReindexCommand = new AsyncRelayCommand(ReindexAsync, () => !_isReindexing);

            _similarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.6) };
            _similarTimer.Tick += OnSimilarTimerTick;

            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); Refresh(); };
        }

        // ===== 전체 재임베딩 =====
        public AsyncRelayCommand ReindexCommand { get; }
        public bool IsReindexing { get => _isReindexing; private set { if (SetProperty(ref _isReindexing, value)) ReindexCommand.RaiseCanExecuteChanged(); } }
        public double ReindexProgress { get => _reindexProgress; private set => SetProperty(ref _reindexProgress, value); }
        public string ReindexStatus { get => _reindexStatus; private set => SetProperty(ref _reindexStatus, value); }

        private async Task ReindexAsync()
        {
            var r = System.Windows.MessageBox.Show(
                "모든 런북 항목을 다시 임베딩(벡터화)합니다.\n기존 벡터 파일은 백업(.bak) 후 새로 만듭니다. 계속할까요?",
                "전체 재임베딩", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (r != System.Windows.MessageBoxResult.Yes) return;

            IsReindexing = true;
            ReindexProgress = 0;
            ReindexStatus = "백업 후 임베딩 준비…";
            try
            {
                int ok = await _service.ReindexAllAsync((done, total) =>
                {
                    ReindexProgress = total > 0 ? done * 100.0 / total : 0;
                    ReindexStatus = $"임베딩 {done}/{total}";
                });
                ReindexProgress = 100;
                ReindexStatus = $"완료 · {ok}개 임베딩됨";
            }
            catch (System.Exception ex)
            {
                ReindexStatus = "오류: " + ex.Message;
            }
            finally { IsReindexing = false; }
        }

        public ObservableCollection<RunbookEntry> Items { get; }

        /// <summary>입력 중 제시되는 비슷한 기존 항목(대체 후보).</summary>
        public ObservableCollection<RunbookSimilarItem> SimilarItems { get; }

        /// <summary>유사 항목 검색 중(스피너 표시용).</summary>
        public bool IsFindingSimilar
        {
            get => _isFindingSimilar;
            private set => SetProperty(ref _isFindingSimilar, value);
        }

        /// <summary>제시할 유사 항목이 있는지.</summary>
        public bool HasSimilar => SimilarItems.Count > 0;

        public string SearchText
        {
            get => _searchText;
            // 값은 즉시 반영(플레이스홀더 즉시 사라짐). 목록 갱신만 디바운스로 미룸.
            set { if (SetProperty(ref _searchText, value)) { _searchTimer.Stop(); _searchTimer.Start(); } }
        }

        /// <summary>선택 카테고리(null=전체). 조치/운영/매뉴얼.</summary>
        public string SelectedCategory
        {
            get => _selectedCategory;
            set { if (SetProperty(ref _selectedCategory, value)) { Refresh(); RaiseCategoryFlags(); } }
        }

        /// <summary>폐기 항목도 보일지. 기본 false(활성만).</summary>
        public bool ShowDeprecated
        {
            get => _showDeprecated;
            set { if (SetProperty(ref _showDeprecated, value)) { Refresh(); OnPropertyChanged(nameof(StatusLabel)); } }
        }

        public string StatusLabel => _showDeprecated ? "전체" : "활성";

        // 카테고리 필터 선택 상태(칩 하이라이트용)
        public bool IsAll => _selectedCategory == null;
        public bool IsAction => _selectedCategory == RunbookCategory.Action;
        public bool IsOperation => _selectedCategory == RunbookCategory.Operation;
        public bool IsManual => _selectedCategory == RunbookCategory.Manual;

        // 카운트 (현황 · 칩 개수)
        public int ActiveCount => _service.Entries.Count(e => e.IsActive);
        public int DeprecatedCount => _service.Entries.Count(e => !e.IsActive);
        public int CountAll => Scope().Count();
        public int CountAction => Scope().Count(e => e.Category == RunbookCategory.Action);
        public int CountOperation => Scope().Count(e => e.Category == RunbookCategory.Operation);
        public int CountManual => Scope().Count(e => e.Category == RunbookCategory.Manual);

        public RelayCommand SelectAllCommand => new RelayCommand(() => SelectedCategory = null);
        public RelayCommand SelectActionCommand => new RelayCommand(() => SelectedCategory = RunbookCategory.Action);
        public RelayCommand SelectOperationCommand => new RelayCommand(() => SelectedCategory = RunbookCategory.Operation);
        public RelayCommand SelectManualCommand => new RelayCommand(() => SelectedCategory = RunbookCategory.Manual);

        // ===== 항목 추가 폼 =====
        public bool IsAddOpen { get => _isAddOpen; set => SetProperty(ref _isAddOpen, value); }
        public bool IsSaving { get => _isSaving; private set { if (SetProperty(ref _isSaving, value)) SaveCommand.RaiseCanExecuteChanged(); } }

        public string NewTitle { get => _newTitle; set { if (SetProperty(ref _newTitle, value)) { SaveCommand.RaiseCanExecuteChanged(); ScheduleSimilaritySearch(); } } }
        public string NewContent { get => _newContent; set { if (SetProperty(ref _newContent, value)) { SaveCommand.RaiseCanExecuteChanged(); ScheduleSimilaritySearch(); } } }
        public string NewCategory { get => _newCategory; set { if (SetProperty(ref _newCategory, value)) RaiseNewCategoryFlags(); } }
        public string NewCode { get => _newCode; set => SetProperty(ref _newCode, value); }
        public string NewModule { get => _newModule; set => SetProperty(ref _newModule, value); }
        public string NewTags { get => _newTags; set => SetProperty(ref _newTags, value); }
        public bool IsNewAction => _newCategory == RunbookCategory.Action;
        public bool IsNewOperation => _newCategory == RunbookCategory.Operation;
        public bool IsNewManual => _newCategory == RunbookCategory.Manual;

        public bool CanSave => !_isSaving && !string.IsNullOrWhiteSpace(_newTitle) && !string.IsNullOrWhiteSpace(_newContent);

        /// <summary>오버레이 헤더 문구. 신규/수정 구분.</summary>
        public string FormTitle => _editingId == null ? "＋ 항목 추가" : "＋ 항목 수정";

        public AsyncRelayCommand SaveCommand { get; }
        public RelayCommand OpenAddCommand => new RelayCommand(() =>
        {
            _editingId = null;
            NewTitle = string.Empty; NewContent = string.Empty; NewCategory = RunbookCategory.Action;
            NewCode = string.Empty; NewModule = string.Empty; NewTags = string.Empty;
            OnPropertyChanged(nameof(FormTitle));
            ClearSimilar();
            IsAddOpen = true;
        });

        /// <summary>기존 항목 수정 — 같은 오버레이를 값 채워 연다.</summary>
        public RelayCommand<RunbookEntry> EditCommand => new RelayCommand<RunbookEntry>(e =>
        {
            if (e == null) return;
            _editingId = e.Id;
            ClearSimilar();
            NewTitle = e.Title ?? string.Empty;
            NewContent = e.Content ?? string.Empty;
            NewCategory = e.Category ?? RunbookCategory.Action;
            NewCode = e.Code ?? string.Empty;
            NewModule = e.Module ?? string.Empty;
            NewTags = e.Tags ?? string.Empty;
            OnPropertyChanged(nameof(FormTitle));
            IsAddOpen = true;
            // 같은 항목을 다시 열면 NewTitle/NewContent 값이 안 바뀌어 세터가 검색을 안 건다.
            // → 열 때마다 명시적으로 유사 항목 검색을 예약한다.
            ScheduleSimilaritySearch();
        });

        /// <summary>폐기(이력 보존). 확인 후 실행.</summary>
        public RelayCommand<RunbookEntry> DeprecateCommand => new RelayCommand<RunbookEntry>(e =>
        {
            if (e == null) return;
            var r = System.Windows.MessageBox.Show(
                $"'{e.Title}' 항목을 폐기할까요?\n답변·검색에서 제외되지만 이력은 남고 언제든 복원할 수 있습니다.",
                "폐기 확인", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (r != System.Windows.MessageBoxResult.Yes) return;
            _service.Deprecate(e.Id);
            Refresh();
        });

        /// <summary>폐기 항목 복원. 확인 없이 바로.</summary>
        public RelayCommand<RunbookEntry> RestoreCommand => new RelayCommand<RunbookEntry>(e =>
        {
            if (e == null) return;
            _service.Restore(e.Id);
            Refresh();
        });

        /// <summary>완전 삭제(되돌릴 수 없음). 강한 경고 후 실행.</summary>
        public RelayCommand<RunbookEntry> DeleteCommand => new RelayCommand<RunbookEntry>(e =>
        {
            if (e == null) return;
            var r = System.Windows.MessageBox.Show(
                $"'{e.Title}' 항목을 완전히 삭제할까요?\n이 작업은 되돌릴 수 없습니다. (이력을 남기려면 '폐기'를 사용하세요)",
                "삭제 확인", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (r != System.Windows.MessageBoxResult.Yes) return;
            _service.Delete(e.Id);
            Refresh();
        });

        public RelayCommand CancelAddCommand => new RelayCommand(() => { IsAddOpen = false; ClearSimilar(); });
        public RelayCommand SetNewActionCommand => new RelayCommand(() => NewCategory = RunbookCategory.Action);
        public RelayCommand SetNewOperationCommand => new RelayCommand(() => NewCategory = RunbookCategory.Operation);
        public RelayCommand SetNewManualCommand => new RelayCommand(() => NewCategory = RunbookCategory.Manual);

        private async Task SaveAsync()
        {
            IsSaving = true;
            try
            {
                string savedId = _editingId;
                if (_editingId == null)
                {
                    var entry = await _service.AddAsync(_newTitle.Trim(), _newCategory, _newContent.Trim(), "사용자",
                        _newCode.Trim(), _newModule.Trim(), _newTags.Trim());
                    savedId = entry?.Id;
                }
                else
                    await _service.UpdateAsync(_editingId, _newTitle.Trim(), _newCategory, _newContent.Trim(),
                        _newCode.Trim(), _newModule.Trim(), _newTags.Trim());

                // 대체: 체크한 유사 항목을 폐기하고 이 항목이 대체한다고 기록.
                var oldIds = SimilarItems.Where(s => s.IsChecked && s.Entry.Id != savedId)
                                         .Select(s => s.Entry.Id).ToList();
                if (savedId != null && oldIds.Count > 0)
                    _service.Supersede(savedId, oldIds);
            }
            finally { IsSaving = false; }
            IsAddOpen = false;
            ClearSimilar();
            Refresh();
        }

        // 입력이 바뀔 때마다 타이머를 재시작 → 멈춘 뒤 0.6초 후 1회만 검색(디바운스).
        private void ScheduleSimilaritySearch()
        {
            _similarTimer.Stop();
            var text = (_newTitle + " " + _newContent).Trim();
            if (text.Length < 2) { ClearSimilar(); return; }
            _similarTimer.Start();
        }

        private async void OnSimilarTimerTick(object sender, EventArgs e)
        {
            _similarTimer.Stop();
            await FindSimilarInternalAsync();
        }

        private async Task FindSimilarInternalAsync()
        {
            var text = (_newTitle + "\n" + _newContent).Trim();
            if (string.IsNullOrWhiteSpace(text)) { ClearSimilar(); return; }

            IsFindingSimilar = true;
            try
            {
                var hits = await _service.FindSimilarAsync(text);
                SimilarItems.Clear();
                if (hits != null)
                    foreach (var h in hits)
                    {
                        if (_editingId != null && h.Entry.Id == _editingId) continue; // 자기 자신 제외
                        SimilarItems.Add(new RunbookSimilarItem(h.Entry, h.Score));
                    }
            }
            catch { SimilarItems.Clear(); /* 임베딩 실패 등 — 조용히 제안 없음 */ }
            finally
            {
                IsFindingSimilar = false;
                OnPropertyChanged(nameof(HasSimilar));
            }
        }

        private void ClearSimilar()
        {
            _similarTimer.Stop();
            IsFindingSimilar = false;
            if (SimilarItems.Count > 0)
            {
                SimilarItems.Clear();
                OnPropertyChanged(nameof(HasSimilar));
            }
        }

        private void RaiseNewCategoryFlags()
        {
            OnPropertyChanged(nameof(IsNewAction));
            OnPropertyChanged(nameof(IsNewOperation));
            OnPropertyChanged(nameof(IsNewManual));
        }

        /// <summary>탭을 처음 열 때 1회 로드.</summary>
        public void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try { _service.Load(); } catch { /* 저장소 없거나 로드 실패 시 빈 목록 */ }
            Refresh();
        }

        private System.Collections.Generic.IEnumerable<RunbookEntry> Scope() =>
            _showDeprecated ? _service.Entries : _service.Entries.Where(e => e.IsActive);

        private void Refresh()
        {
            var list = _service.KeywordSearch(_searchText, _selectedCategory, includeDeprecated: _showDeprecated);
            Items.Clear();
            foreach (var e in list) Items.Add(e);
            RaiseCounts();
        }

        private void RaiseCounts()
        {
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(DeprecatedCount));
            OnPropertyChanged(nameof(CountAll));
            OnPropertyChanged(nameof(CountAction));
            OnPropertyChanged(nameof(CountOperation));
            OnPropertyChanged(nameof(CountManual));
        }

        private void RaiseCategoryFlags()
        {
            OnPropertyChanged(nameof(IsAll));
            OnPropertyChanged(nameof(IsAction));
            OnPropertyChanged(nameof(IsOperation));
            OnPropertyChanged(nameof(IsManual));
        }
    }
}
