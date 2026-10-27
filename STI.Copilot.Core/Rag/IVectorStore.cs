using System.Collections.Generic;

namespace STI.Copilot.Core.Rag
{
    /// <summary>
    /// 청크 + 임베딩을 보관하고 유사도로 검색하는 저장소. 구현은 교체 가능(메모리+파일 기본, 필요시 SQLite 등).
    /// </summary>
    public interface IVectorStore
    {
        int Count { get; }

        /// <summary>등록된 문서 식별자 목록(중복 제거).</summary>
        IReadOnlyList<string> DocumentIds { get; }

        void Add(DocumentChunk chunk);

        /// <summary>특정 문서의 모든 청크 제거(재등록/삭제 시).</summary>
        void RemoveByDocument(string documentId);

        void Clear();

        /// <summary>
        /// 질의 벡터와 가장 유사한 상위 K개를 점수와 함께 반환.
        /// documentId 를 지정하면 해당 문서의 청크 안에서만 검색한다(null 이면 전체).
        /// </summary>
        IReadOnlyList<ScoredChunk> Search(float[] queryEmbedding, int topK, string documentId = null);

        /// <summary>디스크에 저장/로드(영속화). 미지원 구현은 no-op 가능.</summary>
        void Save();
        void Load();
    }
}
