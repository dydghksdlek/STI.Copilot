using STI.Copilot.Wpf.Mvvm;

namespace STI.Copilot.Wpf.ViewModels
{
    /// <summary>
    /// 상위 껍데기 ViewModel. [대화]/[RunBook] 화면 전환을 관리하고 두 화면의 VM을 보유한다.
    /// 창의 DataContext 로 지정되며, 각 화면의 사이드바 탭이 여기에 바인딩해 전환한다.
    /// </summary>
    public class ShellViewModel : ViewModelBase
    {
        private string _activeTab = TabChat;
        private bool _isSidebarExpanded = true;

        public const string TabChat = "chat";
        public const string TabRunbook = "runbook";

        public ShellViewModel(ChatViewModel chat, RunbookViewModel runbook)
        {
            Chat = chat;
            Runbook = runbook;
            ShowChatCommand = new RelayCommand(() => ActiveTab = TabChat);
            ShowRunbookCommand = new RelayCommand(() => ActiveTab = TabRunbook);
            ToggleSidebarCommand = new RelayCommand(() => IsSidebarExpanded = !IsSidebarExpanded);
        }

        /// <summary>사이드바 펼침 여부(대화·RunBook 공용). 타이틀바 토글이 제어.</summary>
        public bool IsSidebarExpanded
        {
            get => _isSidebarExpanded;
            set => SetProperty(ref _isSidebarExpanded, value);
        }

        public RelayCommand ToggleSidebarCommand { get; }

        public ChatViewModel Chat { get; }
        public RunbookViewModel Runbook { get; }

        /// <summary>현재 화면: "chat" 또는 "runbook".</summary>
        public string ActiveTab
        {
            get => _activeTab;
            set
            {
                if (SetProperty(ref _activeTab, value))
                {
                    OnPropertyChanged(nameof(IsChat));
                    OnPropertyChanged(nameof(IsRunbook));
                    if (value == TabRunbook) Runbook.EnsureLoaded();
                }
            }
        }

        public bool IsChat => _activeTab == TabChat;
        public bool IsRunbook => _activeTab == TabRunbook;

        public RelayCommand ShowChatCommand { get; }
        public RelayCommand ShowRunbookCommand { get; }
    }
}
