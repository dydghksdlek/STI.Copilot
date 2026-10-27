using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace STI.Copilot.Core.Rag
{
    /// <summary>
    /// 기본 벡터 저장소: 메모리에 전부 올려두고 완전탐색으로 검색, 바이너리 파일로 영속화.
    /// 외부 의존성 0. 매뉴얼 수준(수천~수만 청크)에선 검색이 수 밀리초라 충분히 빠르다.
    /// </summary>
    public class InMemoryVectorStore : IVectorStore
    {
        private const string Magic = "AIAKB1"; // 파일 포맷 식별자

        private readonly List<DocumentChunk> _chunks = new List<DocumentChunk>();
        private readonly string _filePath;
        private readonly string _embeddingModel;

        /// <summary>파일에 기록돼 있던 임베딩 모델명(로드 후 채워짐). 없으면 null.</summary>
        public string PersistedEmbeddingModel { get; private set; }

        /// <summary>저장된 모델과 현재 모델이 달라 재인덱싱이 필요한 상태인지.</summary>
        public bool NeedsReindex { get; private set; }

        public InMemoryVectorStore(string filePath, string embeddingModel)
        {
            _filePath = filePath;
            _embeddingModel = embeddingModel ?? "";
        }

        public int Count => _chunks.Count;

        public IReadOnlyList<string> DocumentIds =>
            _chunks.Select(c => c.DocumentId).Distinct().ToList();

        public void Add(DocumentChunk chunk)
        {
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));
            _chunks.Add(chunk);
        }

        public void RemoveByDocument(string documentId)
        {
            _chunks.RemoveAll(c => string.Equals(c.DocumentId, documentId, StringComparison.OrdinalIgnoreCase));
        }

        public void Clear() => _chunks.Clear();

        public IReadOnlyList<ScoredChunk> Search(float[] queryEmbedding, int topK, string documentId = null)
        {
            if (queryEmbedding == null || _chunks.Count == 0) return new ScoredChunk[0];
            if (topK <= 0) topK = 1;

            IEnumerable<DocumentChunk> source = _chunks;
            // 특정 문서로 범위를 좁히면 그 문서의 청크만 검색한다.
            if (!string.IsNullOrEmpty(documentId))
                source = _chunks.Where(c => string.Equals(c.DocumentId, documentId, StringComparison.OrdinalIgnoreCase));

            return source
                .Select(c => new ScoredChunk { Chunk = c, Score = VectorMath.Dot(queryEmbedding, c.Embedding) })
                .OrderByDescending(s => s.Score)
                .Take(topK)
                .ToList();
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var fs = new FileStream(_filePath, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(_embeddingModel);
                w.Write(_chunks.Count);
                foreach (var c in _chunks)
                {
                    w.Write(c.DocumentId ?? "");
                    w.Write(c.Index);
                    w.Write(c.Text ?? "");
                    w.Write(c.Language ?? "");
                    var emb = c.Embedding ?? new float[0];
                    w.Write(emb.Length);
                    for (int i = 0; i < emb.Length; i++) w.Write(emb[i]);
                }
            }
            PersistedEmbeddingModel = _embeddingModel;
            NeedsReindex = false;
        }

        public void Load()
        {
            _chunks.Clear();
            NeedsReindex = false;
            PersistedEmbeddingModel = null;
            if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath)) return;

            using (var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
            using (var r = new BinaryReader(fs, Encoding.UTF8))
            {
                var magic = r.ReadString();
                if (magic != Magic) return; // 알 수 없는 포맷 → 빈 상태로 둔다

                PersistedEmbeddingModel = r.ReadString();
                // 임베딩 모델이 다르면 벡터 공간이 달라 섞으면 안 됨 → 로드하지 않고 재인덱싱 요구
                if (!string.Equals(PersistedEmbeddingModel, _embeddingModel, StringComparison.OrdinalIgnoreCase))
                {
                    NeedsReindex = true;
                    return;
                }

                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var chunk = new DocumentChunk
                    {
                        DocumentId = r.ReadString(),
                        Index = r.ReadInt32(),
                        Text = r.ReadString(),
                        Language = r.ReadString()
                    };
                    int len = r.ReadInt32();
                    var emb = new float[len];
                    for (int k = 0; k < len; k++) emb[k] = r.ReadSingle();
                    chunk.Embedding = emb;
                    _chunks.Add(chunk);
                }
            }
        }
    }
}
