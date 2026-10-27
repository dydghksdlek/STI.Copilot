namespace STI.Copilot.Core.Chat
{
    /// <summary>
    /// 대화 한 줄. UI/저장소와 코어가 공유하는 순수 데이터 모델.
    /// </summary>
    public class ChatMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }

        public ChatMessage() { }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }

        public static ChatMessage FromUser(string content) => new ChatMessage(ChatRole.User, content);
        public static ChatMessage FromAssistant(string content) => new ChatMessage(ChatRole.Assistant, content);
        public static ChatMessage FromSystem(string content) => new ChatMessage(ChatRole.System, content);
    }
}
