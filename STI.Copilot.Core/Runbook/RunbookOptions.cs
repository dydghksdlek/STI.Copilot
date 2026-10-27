using System;
using System.IO;

namespace STI.Copilot.Core.Runbook
{
    /// <summary>RunBook 서비스 동작 파라미터.</summary>
    public class RunbookOptions
    {
        /// <summary>임베딩 모델. 문서 RAG 와 동일하게 bge-m3 사용.</summary>
        public string EmbeddingModel { get; set; } = "bge-m3";

        /// <summary>답변 검색 시 가져올 상위 항목 수.</summary>
        public int TopK { get; set; } = 4;

        /// <summary>답변 검색에서 이 점수(코사인) 미만은 관련 없다고 본다. 0~1.</summary>
        public float MinScore { get; set; } = 0.3f;

        /// <summary>대체 후보(유사 항목) 검색 시 상위 개수.</summary>
        public int SimilarTopK { get; set; } = 5;

        /// <summary>대체 후보로 제시할 최소 유사도. 이 값 이상만 후보로 띄운다.</summary>
        public float SimilarMinScore { get; set; } = 0.5f;

        /// <summary>메타데이터 JSON 경로.</summary>
        public string StorePath { get; set; } = DefaultStorePath();

        /// <summary>임베딩(벡터) 저장 경로. 문서용(kb.bin)과 분리해 RunBook만 따로 검색한다.</summary>
        public string VectorStorePath { get; set; } = DefaultVectorPath();

        private static string BaseDir() => CorePaths.DataBaseDir;

        private static string DefaultStorePath() => Path.Combine(BaseDir(), "runbook.json");
        private static string DefaultVectorPath() => Path.Combine(BaseDir(), "runbook.vec");
    }

    /// <summary>검색 결과 한 건: RunBook 항목 + 유사도 점수.</summary>
    public class RunbookHit
    {
        public RunbookEntry Entry { get; set; }
        public float Score { get; set; }
    }
}
