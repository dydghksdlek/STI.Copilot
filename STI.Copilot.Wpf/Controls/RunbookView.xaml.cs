using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace STI.Copilot.Wpf.Controls
{
    /// <summary>RunBook 관리 화면. 창(윈도우) 제어는 헤더 빈 영역 드래그 + 창 버튼으로 처리.</summary>
    public partial class RunbookView : UserControl
    {
        public RunbookView()
        {
            InitializeComponent();
        }

        // 한글 IME 대응: 조합 중(예: ㄴ→내→냉)에는 PropertyChanged 바인딩이 소스를 갱신하지 않으므로
        // TextChanged 마다 바인딩 소스를 강제로 밀어 넣어 즉시 검색되게 한다.
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this);
            if (w != null) w.WindowState = WindowState.Minimized;
        }

        private void MaxRestore_Click(object sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this);
            if (w == null) return;
            w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }

        // 헤더 빈 영역 드래그로 창 이동, 더블클릭으로 최대화/복원.
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsOnInteractiveElement(e.OriginalSource)) return;
            var w = Window.GetWindow(this);
            if (w == null) return;
            if (e.ClickCount == 2)
            {
                w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            try { w.DragMove(); } catch { }
        }

        private static bool IsOnInteractiveElement(object source)
        {
            var d = source as DependencyObject;
            while (d != null)
            {
                if (d is ButtonBase || d is TextBoxBase || d is Selector) return true;
                d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            }
            return false;
        }
    }
}
