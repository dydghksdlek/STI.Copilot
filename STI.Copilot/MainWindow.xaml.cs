using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using STI.Copilot.Core.Documents;
using STI.Copilot.Core.Ollama;
using STI.Copilot.Core.Runbook;
using STI.Copilot.Documents;
using STI.Copilot.Wpf.ViewModels;

namespace STI.Copilot
{
    /// <summary>
    /// 앱 창(쉘). [대화]/[RunBook] 두 화면을 ShellViewModel 로 전환한다.
    /// AllowsTransparency 로 라운드 모서리 + 강조된 그림자를 직접 그린다.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // 문서 추출기 + 옵션 (기존 구성 유지)
            var extractionOptions = new ExtractionOptions();
            var extractors = new DocumentExtractorRegistry()
                .Register(new PlainTextExtractor())
                .Register(new DoclingExtractor(new PdfTextExtractor(), extractionOptions))
                .Register(new DocxTextExtractor());

            // RunBook (코어 서비스). 대화 탭과 RunBook 탭이 같은 인스턴스를 공유한다.
            // 대화창에서 런북 근거로 답하려면 시작 시 미리 로드해 둔다(탭 열기 전에도 검색되게).
            var runbookService = new RunbookService(new OllamaClient());
            try { runbookService.Load(); } catch { /* 저장소 없거나 로드 실패 시 빈 상태로 시작 */ }

            var chatVm = new ChatViewModel(
                extractors: extractors, extractionOptions: extractionOptions, runbookService: runbookService);
            var runbookVm = new RunbookViewModel(runbookService);

            DataContext = new ShellViewModel(chatVm, runbookVm);

            StateChanged += OnStateChanged;
        }

        // 최대화 시엔 그림자 여백·라운드를 없애 화면을 꽉 채운다.
        private void OnStateChanged(object sender, EventArgs e)
        {
            bool max = WindowState == WindowState.Maximized;
            RootBorder.Margin = max ? new Thickness(0) : new Thickness(22);
            RootBorder.CornerRadius = new CornerRadius(max ? 0 : 12);
            UpdateClip();
        }

        private void ContentHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateClip();

        // 내용을 라운드 모서리로 잘라낸다.
        private void UpdateClip()
        {
            double r = WindowState == WindowState.Maximized ? 0 : 12;
            ContentHost.Clip = new RectangleGeometry(
                new Rect(0, 0, ContentHost.ActualWidth, ContentHost.ActualHeight), r, r);
        }

        // === 타이틀바 창 제어 ===
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaxRestore_Click(object sender, RoutedEventArgs e) => ToggleMaxRestore();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void ToggleMaxRestore() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        // 타이틀바 빈 영역 드래그로 창 이동, 더블클릭으로 최대화/복원. (버튼 위 클릭은 제외)
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsOnInteractiveElement(e.OriginalSource)) return;
            if (e.ClickCount == 2) { ToggleMaxRestore(); return; }
            try { DragMove(); } catch { /* 상태 전환 중 호출되면 무시 */ }
        }

        private static bool IsOnInteractiveElement(object source)
        {
            var d = source as DependencyObject;
            while (d != null)
            {
                if (d is ButtonBase) return true;
                d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            }
            return false;
        }
    }
}
