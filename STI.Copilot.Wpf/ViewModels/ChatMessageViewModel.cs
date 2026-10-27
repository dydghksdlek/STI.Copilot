using STI.Copilot.Core.Chat;
using STI.Copilot.Wpf.Mvvm;

namespace STI.Copilot.Wpf.ViewModels
{
    /// <summary>화면에 보여줄 메시지 한 줄. 스트리밍 중 Content 가 실시간으로 갱신된다.</summary>
    public class ChatMessageViewModel : ViewModelBase
    {
        private string _content;
        private bool _isGenerating;

        public ChatMessageViewModel(string role, string content)
        {
            Role = role;
            _content = content;
        }

        public string Role { get; }

        public bool IsUser => Role == ChatRole.User;
        public bool IsAssistant => Role == ChatRole.Assistant;

        public string Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }

        /// <summary>답변 생성 중 여부. 말풍선에 "…" 타이핑 표시를 켜고 끄는 데 쓴다.</summary>
        public bool IsGenerating
        {
            get => _isGenerating;
            set => SetProperty(ref _isGenerating, value);
        }
    }
}
