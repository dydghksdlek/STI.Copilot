using System.Threading;
using System.Threading.Tasks;

namespace STI.Copilot.Core.Chat
{
    /// <summary>
    /// 질문마다 ChatService 에 주입할 문맥(근거 문서 등)을 생성한다.
    /// RAG 는 이 인터페이스로 붙는다. null 이면 순수 채팅으로 동작한다.
    /// </summary>
    public interface IChatContextProvider
    {
        /// <summary>사용자 질문에 대해 주입할 문맥 문자열을 반환. 관련 내용이 없으면 빈 문자열.</summary>
        Task<string> GetContextAsync(string userMessage, CancellationToken ct = default);
    }
}
