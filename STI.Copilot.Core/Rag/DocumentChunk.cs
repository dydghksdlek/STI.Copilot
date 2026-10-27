namespace STI.Copilot.Core.Rag
{
    /// <summary>
    /// 문서를 잘게 나눈 조각 하나와 그 의미 벡터. 벡터 저장소의 기본 단위.
    /// </summary>
    public class DocumentChunk
    {
        /// <summary>출처 문서 식별자(보통 파일명). 삭제·필터·출처표시에 사용.</summary>
        public string DocumentId { get; set; }

        /// <summary>문서 내 조각 순번.</summary>
        public int Index { get; set; }

        /// <summary>조각 원문 텍스트.</summary>
        public string Text { get; set; }

        /// <summary>임베딩 벡터(L2 정규화 저장). 유사도는 내적으로 계산.</summary>
        public float[] Embedding { get; set; }

        /// <summary>선택: 언어 태그("ko","en" 등). 필터/출처표시에 사용, 없어도 됨.</summary>
        public string Language { get; set; }
    }

    /// <summary>검색 결과 한 건: 조각 + 유사도 점수.</summary>
    public class ScoredChunk
    {
        public DocumentChunk Chunk { get; set; }
        public float Score { get; set; }
    }
}
