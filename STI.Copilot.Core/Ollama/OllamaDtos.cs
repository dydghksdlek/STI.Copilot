using System.Runtime.Serialization;

namespace STI.Copilot.Core.Ollama
{
    // Ollama REST 요청/응답용 DTO.
    // 외부 NuGet 없이 DataContractJsonSerializer 로 직렬화하기 위해 [DataContract]/[DataMember] 로 필드명을 지정한다.
    // 응답에 우리가 선언하지 않은 필드가 와도 무시되므로 안전하다.

    [DataContract]
    internal class ChatRequestDto
    {
        [DataMember(Name = "model", Order = 0)]
        public string Model { get; set; }

        [DataMember(Name = "messages", Order = 1)]
        public MessageDto[] Messages { get; set; }

        [DataMember(Name = "stream", Order = 2)]
        public bool Stream { get; set; }

        // null 이면 직렬화에서 제외.
        [DataMember(Name = "options", Order = 3, EmitDefaultValue = false)]
        public OllamaRequestOptionsDto Options { get; set; }
    }

    [DataContract]
    internal class MessageDto
    {
        [DataMember(Name = "role", Order = 0)]
        public string Role { get; set; }

        [DataMember(Name = "content", Order = 1)]
        public string Content { get; set; }
    }

    // /api/chat 스트리밍의 한 줄(NDJSON)에 대응.
    [DataContract]
    internal class ChatStreamChunkDto
    {
        [DataMember(Name = "message")]
        public MessageDto Message { get; set; }

        [DataMember(Name = "done")]
        public bool Done { get; set; }
    }

    [DataContract]
    internal class EmbedRequestDto
    {
        [DataMember(Name = "model", Order = 0)]
        public string Model { get; set; }

        [DataMember(Name = "prompt", Order = 1)]
        public string Prompt { get; set; }

        // null 이면 직렬화에서 제외(EmitDefaultValue=false) → 옵션 미지정과 동일.
        [DataMember(Name = "options", Order = 2, EmitDefaultValue = false)]
        public OllamaRequestOptionsDto Options { get; set; }
    }

    // 모델 언로드용(POST /api/generate, keep_alive=0). GPU↔CPU 전환 시 재로딩 유도에 사용.
    [DataContract]
    internal class GenerateUnloadDto
    {
        [DataMember(Name = "model", Order = 0)]
        public string Model { get; set; }

        [DataMember(Name = "keep_alive", Order = 1)]
        public int KeepAlive { get; set; }
    }

    [DataContract]
    internal class OllamaRequestOptionsDto
    {
        // nullable + EmitDefaultValue=false → null 이면 직렬화 제외, 0 은 그대로 전송(num_gpu=0=CPU 유지).
        /// <summary>GPU 레이어 수. 0 = CPU 강제. null = 미지정(Ollama 자동).</summary>
        [DataMember(Name = "num_gpu", Order = 0, EmitDefaultValue = false)]
        public int? NumGpu { get; set; }

        /// <summary>컨텍스트 창 크기(토큰). 기본값이 작으면 RAG 프롬프트+긴 답변이 잘리므로 넉넉히 준다.</summary>
        [DataMember(Name = "num_ctx", Order = 1, EmitDefaultValue = false)]
        public int? NumCtx { get; set; }
    }

    [DataContract]
    internal class EmbedResponseDto
    {
        [DataMember(Name = "embedding")]
        public double[] Embedding { get; set; }
    }

    [DataContract]
    internal class TagsResponseDto
    {
        [DataMember(Name = "models")]
        public TagModelDto[] Models { get; set; }
    }

    [DataContract]
    internal class TagModelDto
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }
    }

    // 오류 응답 본문(예: {"error":"\"bge-m3\" does not support generate"}).
    [DataContract]
    internal class ErrorResponseDto
    {
        [DataMember(Name = "error")]
        public string Error { get; set; }
    }
}
