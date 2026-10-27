using System;
using System.IO;

namespace STI.Copilot.Core.Rag
{
    /// <summary>RAG 동작 파라미터. 문서 성격에 맞게 조정한다.</summary>
    public class RagOptions
    {
        /// <summary>검색해서 프롬프트에 넣을 상위 청크 개수.</summary>
        public int TopK { get; set; } = 4;

        /// <summary>이 점수(코사인 유사도) 미만은 관련 없다고 보고 버린다. 0~1. 문서에 맞게 조정.</summary>
        public float MinScore { get; set; } = 0.3f;

        /// <summary>벡터 저장소 파일 경로. 기본 C:\STI_Copilot\kb.bin.</summary>
        public string StorePath { get; set; } = DefaultStorePath();

        /// <summary>답변 시 근거 문서가 영어여도 한국어로 답하게 할지.</summary>
        public bool AnswerInKorean { get; set; } = true;

        private static string DefaultStorePath() => Path.Combine(CorePaths.DataBaseDir, "kb.bin");
    }
}
