using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using STI.Copilot.Core.Ollama;

namespace STI.Copilot.Core.Chat
{
    /// <summary>
    /// 대화 흐름을 관리하는 상위 서비스. 히스토리 보관 + 시스템 프롬프트 + 모델 호출을 캡슐화한다.
    /// 어떤 UI(껍데기)든 이 클래스만 호출하면 된다. RAG 는 이후 이 층에 문맥을 주입해 확장한다.
    /// </summary>
    public class ChatService
    {
        private readonly OllamaClient _client;
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private string _systemPrompt;

        public ChatService(OllamaClient client, string systemPrompt = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            SystemPrompt = systemPrompt ??
                "당신은 한국어로 답하는 어시스턴트입니다. 정확하고 간결하게 답하세요. " +
                "반드시 한국어로만 답하고, 일본어(가나)·중국어(한자) 문자를 절대 섞지 마세요. " +
                "외래어·기술용어도 한글로 표기하세요(예: 트랜스미션, 밸브, 시퀀스).";
        }

        /// <summary>대화에 사용할 모델. null 이면 OllamaClient 옵션의 기본값 사용.</summary>
        public string Model { get; set; }

        /// <summary>시스템 프롬프트. 변경하면 다음 요청부터 반영된다.</summary>
        public string SystemPrompt
        {
            get => _systemPrompt;
            set => _systemPrompt = value;
        }

        /// <summary>지금까지의 사용자/어시스턴트 대화 내역(시스템 메시지 제외).</summary>
        public IReadOnlyList<ChatMessage> History => _history;

        /// <summary>대화 내역을 비운다.</summary>
        public void Reset() => _history.Clear();

        /// <summary>
        /// 문맥 주입기(RAG 등). 설정하면 매 질문마다 관련 문서를 검색해 프롬프트에 넣는다.
        /// null 이면 순수 채팅으로 동작한다.
        /// </summary>
        public IChatContextProvider ContextProvider { get; set; }

        /// <summary>
        /// 사용자 메시지를 보내고 스트리밍으로 답변을 받는다.
        /// onToken 으로 조각이 도착하며, 완료되면 전체 답변 문자열을 반환한다.
        /// </summary>
        public async Task<string> AskAsync(
            string userMessage,
            Action<string> onToken = null,
            CancellationToken ct = default,
            Action onContextReady = null)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                throw new ArgumentException("메시지가 비어 있습니다.", nameof(userMessage));

            // 이번 턴에만 쓸 문맥 검색(있으면). 이 문맥은 히스토리에 저장하지 않는다.
            string context = "";
            if (ContextProvider != null)
                context = await ContextProvider.GetContextAsync(userMessage, ct).ConfigureAwait(false);

            // 문맥 검색 완료 시점 알림(스트리밍 시작 전).
            // 예: 런북 "전체 목록"을 답변 앞에 먼저 출력한 뒤 상세를 스트리밍하기 위함.
            onContextReady?.Invoke();

            var request = BuildRequestMessages(userMessage, context);

            var full = new System.Text.StringBuilder();
            await _client.ChatStreamAsync(
                request,
                token =>
                {
                    full.Append(token);
                    onToken?.Invoke(token);
                },
                Model,
                ct).ConfigureAwait(false);

            var answer = full.ToString();
            // 히스토리엔 원문 질문 + 답변만 저장(검색 문맥은 버려 토큰 낭비/오염 방지)
            _history.Add(ChatMessage.FromUser(userMessage));
            _history.Add(ChatMessage.FromAssistant(answer));
            return answer;
        }

        // 시스템 프롬프트 + 지난 히스토리 + (문맥) + 이번 질문 순으로 요청 메시지를 만든다.
        private IEnumerable<ChatMessage> BuildRequestMessages(string currentUserMessage, string context)
        {
            var messages = new List<ChatMessage>();
            if (!string.IsNullOrWhiteSpace(_systemPrompt))
                messages.Add(ChatMessage.FromSystem(_systemPrompt));

            messages.AddRange(_history); // 지난 대화(이번 질문은 아직 미포함)

            if (!string.IsNullOrEmpty(context))
            {
                messages.Add(ChatMessage.FromSystem(
                    "너는 아래 [근거]에 있는 내용만 사용해 한국어로 답하는 도우미다.\n" +
                    "- 질문과 관련된 항목의 **실제 내용(절차·방법·수치·주의사항)을 구체적으로 풀어서** 답하라. 제목이나 번호만 나열하지 말 것.\n" +
                    "- 관련 항목이 여러 개면 각 항목의 핵심 내용을 항목별로 정리해 적어라.\n" +
                    "- 근거가 영어로 되어 있어도 반드시 한국어로만 답하라. 일본어(가나)·중국어(한자) 문자를 절대 섞지 말고, 외래어·기술용어도 한글로 표기하라(예: 트랜스미션, 밸브).\n" +
                    "- 근거에 없는 내용은 지어내지 말고, 관련 내용이 전혀 없으면 '등록된 런북·문서에서 찾을 수 없습니다'라고만 답하라.\n" +
                    "- 런북과 문서의 내용이 상충하면 런북을 우선한다.\n\n[근거]\n" + context));
            }

            messages.Add(ChatMessage.FromUser(currentUserMessage));
            return messages;
        }
    }
}
