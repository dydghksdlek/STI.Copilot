using System;

namespace STI.Copilot.Core.Ollama
{
    /// <summary>
    /// Ollama 접속/모델 설정. 장비마다 서버 주소나 모델이 다를 수 있으므로 외부에서 주입한다.
    /// </summary>
    public class OllamaOptions
    {
        /// <summary>Ollama REST 서버 주소. 기본값은 로컬.</summary>
        public string BaseUrl { get; set; } = "http://localhost:11434";

        /// <summary>대화/생성에 사용할 기본 모델 이름. 일반(non-coder) 모델이 한국어·요약·근거답변에 유리.
        /// qwen3:14b = Apache 2.0(상업 배포 가능) · 한국어 강함. CPU로는 느리니 GPU 사용 권장.
        /// (미설치면 앱이 모델 목록의 첫 항목으로 자동 대체한다. → 배포 PC에 먼저 'ollama pull qwen3:14b')</summary>
        public string ChatModel { get; set; } = "qwen3:14b";

        /// <summary>임베딩(RAG 검색)에 사용할 모델 이름 (예: "bge-m3").</summary>
        public string EmbeddingModel { get; set; } = "bge-m3";

        /// <summary>
        /// 임베딩 요청의 GPU 레이어 수. 0 = CPU 강제. null = Ollama 자동(GPU 우선).
        /// 기본 0: 일부 구형 GPU(예: GTX 1060/Pascal)는 임베딩 모델을 GPU에 올릴 때 크래시(500)하므로
        /// 안전하게 CPU로 돌린다. bge-m3는 가벼워 CPU로도 빠르다. 최신 GPU면 null 로 바꿔도 된다.
        /// </summary>
        public int? EmbedNumGpu { get; set; } = 0;

        /// <summary>
        /// 채팅(생성) 요청의 GPU 레이어 수. 0 = CPU 강제. null = Ollama 자동(GPU 우선).
        /// 기본 0: 임베딩과 동일 이유(GTX 1060 등 구형 GPU가 CUDA 크래시 'no kernel image').
        /// 최신 GPU면 null 로 바꿔 GPU 사용. 단 CPU 채팅은 느리다(특히 14b) → 작은 모델(7b/3b) 권장.
        /// </summary>
        public int? ChatNumGpu { get; set; } = 0;

        /// <summary>
        /// 채팅 컨텍스트 창 크기(토큰). null = Ollama 기본값 사용.
        /// RAG 는 프롬프트(시스템+근거+질문)가 커서 기본값(작음)이면 답변이 중간에 잘린다.
        /// 8192 면 근거 상위 몇 건 + 긴 한국어 답변을 담기에 넉넉하다(VRAM/RAM 여유에 따라 조절).
        /// </summary>
        public int? ChatNumCtx { get; set; } = 8192;

        /// <summary>요청 타임아웃. 로컬 모델은 첫 응답이 느릴 수 있어 넉넉하게 둔다.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
    }
}
