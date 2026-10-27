namespace STI.Copilot.Core.Chat
{
    /// <summary>
    /// 대화 메시지의 역할. Ollama /api/chat 의 role 값과 1:1로 대응된다.
    /// </summary>
    public static class ChatRole
    {
        public const string System = "system";
        public const string User = "user";
        public const string Assistant = "assistant";
    }
}
