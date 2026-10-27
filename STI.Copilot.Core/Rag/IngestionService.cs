using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using STI.Copilot.Core.Documents;
using STI.Copilot.Core.Ollama;

namespace STI.Copilot.Core.Rag
{
    /// <summary>
    /// 문서 등록 파이프라인: 텍스트 추출 → 청크 분할 → 임베딩 → 벡터 저장소 적재 → 파일 저장.
    /// </summary>
    public class IngestionService
    {
        private readonly OllamaClient _client;
        private readonly DocumentExtractorRegistry _extractors;
        private readonly TextChunker _chunker;
        private readonly IVectorStore _store;

        public IngestionService(
            OllamaClient client,
            DocumentExtractorRegistry extractors,
            IVectorStore store,
            TextChunker chunker = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _extractors = extractors ?? throw new ArgumentNullException(nameof(extractors));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _chunker = chunker ?? new TextChunker();
        }

        /// <summary>지원 가능한 파일인지.</summary>
        public bool CanHandle(string filePath) => _extractors.CanHandle(filePath);

        /// <summary>
        /// 파일 하나를 등록한다. onProgress(현재, 전체)로 임베딩 진행률을 알린다.
        /// 같은 문서를 다시 등록하면 기존 청크를 먼저 제거해 중복을 막는다.
        /// </summary>
        public async Task<int> IngestAsync(
            string filePath,
            Action<int, int> onProgress = null,
            CancellationToken ct = default)
        {
            // 텍스트 추출은 무거울 수 있다(예: Docling 파이썬 프로세스 대기 수십 초).
            // UI 스레드에서 동기로 돌면 화면이 얼어붙으므로 백그라운드 스레드로 오프로드한다.
            var text = await Task.Run(() => _extractors.Extract(filePath), ct).ConfigureAwait(false);
            var docId = Path.GetFileName(filePath);
            var chunks = _chunker.Split(text).ToList();

            _store.RemoveByDocument(docId);

            try
            {
                for (int i = 0; i < chunks.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var emb = await _client.EmbedAsync(chunks[i], null, ct).ConfigureAwait(false);
                    VectorMath.NormalizeInPlace(emb);
                    _store.Add(new DocumentChunk
                    {
                        DocumentId = docId,
                        Index = i,
                        Text = chunks[i],
                        Embedding = emb
                    });
                    onProgress?.Invoke(i + 1, chunks.Count);
                }
            }
            catch (OperationCanceledException)
            {
                // 취소 시 절반만 들어간 문서를 제거해 저장소를 깨끗이 유지한다.
                _store.RemoveByDocument(docId);
                throw;
            }

            _store.Save();
            return chunks.Count;
        }

        /// <summary>여러 파일을 순서대로 등록.</summary>
        public async Task IngestManyAsync(
            IEnumerable<string> filePaths,
            Action<string, int, int> onProgress = null,
            CancellationToken ct = default)
        {
            foreach (var path in filePaths)
            {
                var name = Path.GetFileName(path);
                await IngestAsync(path, (done, total) => onProgress?.Invoke(name, done, total), ct)
                    .ConfigureAwait(false);
            }
        }
    }
}
