using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace STI.Copilot.Core.Chat
{
    /// <summary>
    /// 여러 근거 제공자(런북·문서)를 하나로 묶는다. 켜진 소스만 검색해 합치며,
    /// 런북을 먼저 넣어 우선순위를 준다. 하나 이상 켜졌는데 아무 근거도 없으면
    /// "근거 없음" 표식을 반환해, 모델이 지어내지 않고 "찾을 수 없음"으로 답하게 한다.
    /// (둘 다 꺼져 있으면 빈 문자열 → ChatService 는 순수 채팅으로 동작.)
    /// </summary>
    public class CompositeContextProvider : IChatContextProvider
    {
        private readonly IChatContextProvider _runbook;
        private readonly IChatContextProvider _documents;

        public CompositeContextProvider(IChatContextProvider runbook, IChatContextProvider documents)
        {
            _runbook = runbook;
            _documents = documents;
        }

        public bool IncludeRunbook { get; set; }
        public bool IncludeDocuments { get; set; }

        /// <summary>근거를 못 찾았을 때 넣는 표식(엄격 답변 유도용).</summary>
        public const string NoSourceMarker = "(체크한 런북·문서에서 관련 내용을 찾지 못했습니다.)";

        public async Task<string> GetContextAsync(string userMessage, CancellationToken ct = default)
        {
            if (!IncludeRunbook && !IncludeDocuments) return "";

            var sb = new StringBuilder();

            if (IncludeRunbook && _runbook != null)
            {
                var c = await _runbook.GetContextAsync(userMessage, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(c)) sb.AppendLine("[런북]").AppendLine(c).AppendLine();
            }
            if (IncludeDocuments && _documents != null)
            {
                var c = await _documents.GetContextAsync(userMessage, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(c)) sb.AppendLine("[문서]").AppendLine(c);
            }

            var text = sb.ToString().Trim();
            return text.Length == 0 ? NoSourceMarker : text;
        }
    }
}
