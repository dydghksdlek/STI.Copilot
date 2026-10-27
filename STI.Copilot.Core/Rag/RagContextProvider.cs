using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using STI.Copilot.Core.Chat;
using STI.Copilot.Core.Ollama;

namespace STI.Copilot.Core.Rag
{
    /// <summary>
    /// 질문을 임베딩해 벡터 저장소에서 관련 청크를 찾아 문맥 문자열로 만든다.
    /// ChatService.ContextProvider 에 꽂으면 RAG 로 동작한다.
    /// </summary>
    public class RagContextProvider : IChatContextProvider
    {
        private readonly OllamaClient _client;
        private readonly IVectorStore _store;
        private readonly RagOptions _options;

        public RagContextProvider(OllamaClient client, IVectorStore store, RagOptions options = null)
        {
            _client = client;
            _store = store;
            _options = options ?? new RagOptions();
        }

        /// <summary>직전 검색에서 실제 사용된 근거 청크(출처 표시용).</summary>
        public IReadOnlyList<ScoredChunk> LastSources { get; private set; } = new ScoredChunk[0];

        /// <summary>
        /// 검색 범위를 특정 문서로 제한한다. null 이면 전체 문서에서 검색한다.
        /// (여러 문서를 올려놓고 한 문서만 물어볼 때 다른 문서가 딸려오는 것을 막는다.)
        /// </summary>
        public string DocumentFilter { get; set; }

        public async Task<string> GetContextAsync(string userMessage, CancellationToken ct = default)
        {
            LastSources = new ScoredChunk[0];
            if (_store.Count == 0 || string.IsNullOrWhiteSpace(userMessage))
                return "";

            // model=null 이면 OllamaClient 옵션의 EmbeddingModel(bge-m3) 사용
            var queryVec = await _client.EmbedAsync(userMessage, null, ct).ConfigureAwait(false);
            VectorMath.NormalizeInPlace(queryVec);

            var hits = _store.Search(queryVec, _options.TopK, DocumentFilter);
            var relevant = new List<ScoredChunk>();
            foreach (var h in hits)
                if (h.Score >= _options.MinScore) relevant.Add(h);

            if (relevant.Count == 0) return "";
            LastSources = relevant;

            var sb = new StringBuilder();
            for (int i = 0; i < relevant.Count; i++)
            {
                var c = relevant[i].Chunk;
                sb.Append('(').Append(i + 1).Append(") ").Append(c.DocumentId).Append(": ")
                  .AppendLine(c.Text);
            }
            return sb.ToString().Trim();
        }
    }
}
