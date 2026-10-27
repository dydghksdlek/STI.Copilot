using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using STI.Copilot.Wpf.ViewModels;

namespace STI.Copilot.Wpf.Controls
{
    /// <summary>
    /// 드롭인 채팅 UserControl. DataContext 를 지정하지 않으면 기본 ChatViewModel 을 스스로 생성한다.
    /// 장비 프로그램에서는 &lt;controls:ChatView/&gt; 하나만 배치하면 된다.
    /// 옵션(서버 주소/모델 등)을 바꾸려면 호스트에서 DataContext 에 ChatViewModel 을 직접 넣으면 된다.
    /// </summary>
    public partial class ChatView : UserControl
    {
        private bool _stickToBottom = true;
        private Window _hostWindow;

        public ChatView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // === 창(윈도우) 제어용 속성 ===
        // OS 타이틀바를 제거한 호스트(MainWindow)에서 True 로 켜면 헤더에 창 버튼(─ ▢ ✕)이 표시된다.
        // 장비 프로그램에 재사용할 때는 기본값 False 라 창 버튼이 나오지 않는다.
        public static readonly DependencyProperty ShowWindowControlsProperty =
            DependencyProperty.Register(nameof(ShowWindowControls), typeof(bool), typeof(ChatView),
                new PropertyMetadata(false));

        public bool ShowWindowControls
        {
            get => (bool)GetValue(ShowWindowControlsProperty);
            set => SetValue(ShowWindowControlsProperty, value);
        }

        // 상단 좌측에 표시할 작은 아이콘(일반 이미지). 창/프로그램 아이콘이 아니라 단순 Image 소스다.
        public static readonly DependencyProperty TitleIconProperty =
            DependencyProperty.Register(nameof(TitleIcon), typeof(ImageSource), typeof(ChatView),
                new PropertyMetadata(null));

        public ImageSource TitleIcon
        {
            get => (ImageSource)GetValue(TitleIconProperty);
            set => SetValue(TitleIconProperty, value);
        }

        // 최대화 여부(창 버튼 아이콘 최대화↔복원 전환용). 코드비하인드가 창 상태에 맞춰 갱신한다.
        public static readonly DependencyProperty IsMaximizedProperty =
            DependencyProperty.Register(nameof(IsMaximized), typeof(bool), typeof(ChatView),
                new PropertyMetadata(false));

        public bool IsMaximized
        {
            get => (bool)GetValue(IsMaximizedProperty);
            set => SetValue(IsMaximizedProperty, value);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!(DataContext is ChatViewModel vm))
            {
                vm = new ChatViewModel();
                DataContext = vm;
            }

            // 호스트 창을 잡아 최대화 상태를 추적(창 버튼 아이콘 갱신).
            _hostWindow = Window.GetWindow(this);
            if (_hostWindow != null)
            {
                _hostWindow.StateChanged -= HostWindow_StateChanged;
                _hostWindow.StateChanged += HostWindow_StateChanged;
                IsMaximized = _hostWindow.WindowState == WindowState.Maximized;
            }

            await vm.LoadModelsAsync();
        }

        private void HostWindow_StateChanged(object sender, System.EventArgs e)
        {
            if (_hostWindow != null)
                IsMaximized = _hostWindow.WindowState == WindowState.Maximized;
        }

        // === 커스텀 창 버튼 ===
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this);
            if (w != null) w.WindowState = WindowState.Minimized;
        }

        private void MaxRestore_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaxRestore(Window.GetWindow(this));
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }

        private static void ToggleMaxRestore(Window w)
        {
            if (w == null) return;
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        // 헤더 빈 영역 드래그로 창 이동, 더블클릭으로 최대화/복원. (버튼 위 클릭은 무시)
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!ShowWindowControls) return;               // 재사용 호스트에선 드래그 이동 비활성
            if (IsOnInteractiveElement(e.OriginalSource)) return;

            var w = Window.GetWindow(this);
            if (w == null) return;

            if (e.ClickCount == 2)
            {
                ToggleMaxRestore(w);
                return;
            }
            try { w.DragMove(); } catch { /* 상태 전환 중 호출되면 무시 */ }
        }

        // 클릭 지점이 버튼/입력 등 상호작용 요소 위인지 확인(그 경우 창 드래그를 하지 않음).
        private static bool IsOnInteractiveElement(object source)
        {
            var d = source as DependencyObject;
            while (d != null)
            {
                if (d is ButtonBase || d is TextBoxBase || d is System.Windows.Controls.Primitives.Selector)
                    return true;
                d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        // Enter = 전송, Shift+Enter = 줄바꿈
        private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                if (DataContext is ChatViewModel vm && vm.SendCommand.CanExecute(null))
                    vm.SendCommand.Execute(null);
            }
        }

        // 스트리밍 중 바닥에 붙어 있으면 자동으로 따라 내려간다.
        private void MessagesScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 사용자가 위로 스크롤하면 자동 스크롤 해제, 바닥으로 오면 재개
            if (e.ExtentHeightChange == 0)
                _stickToBottom = MessagesScroll.VerticalOffset >= MessagesScroll.ScrollableHeight - 1;

            if (_stickToBottom && e.ExtentHeightChange != 0)
                MessagesScroll.ScrollToVerticalOffset(MessagesScroll.ExtentHeight);
        }

        // === 드래그앤드롭으로 문서 등록 ===
        // 파일을 끌고 오는 동안 복사 커서를 표시하고, 드롭존을 강조한다.
        private void Dropzone_DragOver(object sender, DragEventArgs e)
        {
            bool hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
            if (hasFiles && sender is FrameworkElement fe)
                fe.Opacity = 0.6; // 드롭 대상 위에 있음을 알리는 미세 하이라이트
            e.Handled = true;
        }

        private void Dropzone_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe) fe.Opacity = 1.0;
        }

        private async void Dropzone_Drop(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe) fe.Opacity = 1.0;
            if (!(DataContext is ChatViewModel vm)) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            await vm.IngestFilesAsync(files);
        }
    }
}
