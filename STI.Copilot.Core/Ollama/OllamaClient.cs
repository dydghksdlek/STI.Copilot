using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using STI.Copilot.Core.Chat;

namespace STI.Copilot.Core.Ollama
{
    /// <summary>
    /// Ollama 로컬 REST API 클라이언트. HttpClient 만으로 통신하며 UI/외부 패키지 의존성이 없다.
    /// 이 클래스가 "이식 가능한 핵심"이다.
    /// </summary>
    public class OllamaClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly OllamaOptions _options;
        private bool _disposed;

        public OllamaClient(OllamaOptions options = null)
        {
            _options = options ?? new OllamaOptions();
            _http = new HttpClient
            {
                BaseAddress = new Uri(_options.BaseUrl),
                Timeout = _options.Timeout
            };
        }

        /// <summary>설치된 모델 이름 목록을 가져온다. (연결 확인용으로도 유용)</summary>
        public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
        {
            using (var resp = await _http.GetAsync("/api/tags", ct).ConfigureAwait(false))
            {
                await EnsureSuccessOrThrowAsync(resp).ConfigureAwait(false);
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var dto = JsonHelper.Deserialize<TagsResponseDto>(json);
                if (dto?.Models == null) return new string[0];
                return dto.Models.Select(m => m.Name).ToList();
            }
        }

        /// <summary>
        /// 모델을 메모리에서 즉시 내린다(keep_alive=0). GPU↔CPU 전환 시 다음 요청이
        /// 새 설정으로 재로딩되도록 유도할 때 쓴다. 베스트에포트 — 실패해도 예외를 던지지 않는다.
        /// </summary>
        public async Task UnloadAsync(string model, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(model)) return;
            try
            {
                var dto = new GenerateUnloadDto { Model = model, KeepAlive = 0 };
                var json = JsonHelper.Serialize(dto);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var resp = await _http.PostAsync("/api/generate", content, ct).ConfigureAwait(false))
                {
                    // 결과는 무시(언로드가 목적).
                }
            }
            catch { /* 언로드 실패는 조용히 무시 */ }
        }

        /// <summary>
        /// 대화를 스트리밍으로 생성한다. 토큰이 도착할 때마다 onToken 이 호출된다.
        /// </summary>
        /// <param name="messages">system/user/assistant 히스토리 전체</param>
        /// <param name="onToken">부분 응답 조각 콜백 (UI 스레드 마샬링은 호출측 책임)</param>
        /// <param name="model">모델 이름. null 이면 옵션의 ChatModel 사용</param>
        public async Task ChatStreamAsync(
            IEnumerable<ChatMessage> messages,
            Action<string> onToken,
            string model = null,
            CancellationToken ct = default)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (onToken == null) throw new ArgumentNullException(nameof(onToken));

            var request = new ChatRequestDto
            {
                Model = string.IsNullOrWhiteSpace(model) ? _options.ChatModel : model,
                Stream = true,
                Messages = messages.Select(m => new MessageDto { Role = m.Role, Content = m.Content }).ToArray()
            };
            // 옵션: num_gpu(0=CPU 강제, 구형 GPU 회피) + num_ctx(컨텍스트 크기, 답변 잘림 방지).
            var chatOpts = new OllamaRequestOptionsDto();
            if (_options.ChatNumGpu.HasValue) chatOpts.NumGpu = _options.ChatNumGpu.Value;
            if (_options.ChatNumCtx.HasValue && _options.ChatNumCtx.Value > 0) chatOpts.NumCtx = _options.ChatNumCtx.Value;
            if (chatOpts.NumGpu.HasValue || chatOpts.NumCtx.HasValue) request.Options = chatOpts;

            var payload = JsonHelper.Serialize(request);

            using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            })
            using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                await EnsureSuccessOrThrowAsync(resp).ConfigureAwait(false);
                using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    // Ollama 는 NDJSON: 한 줄에 완결된 JSON 객체 하나씩 흘려보낸다.
                    string line;
                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var chunk = JsonHelper.Deserialize<ChatStreamChunkDto>(line);
                        var piece = chunk?.Message?.Content;
                        if (!string.IsNullOrEmpty(piece))
                            onToken(piece);

                        if (chunk != null && chunk.Done)
                            break;
                    }
                }
            }
        }

        /// <summary>대화를 한 번에(비스트리밍) 생성해 전체 문자열을 반환한다.</summary>
        public async Task<string> ChatAsync(
            IEnumerable<ChatMessage> messages,
            string model = null,
            CancellationToken ct = default)
        {
            var sb = new StringBuilder();
            await ChatStreamAsync(messages, t => sb.Append(t), model, ct).ConfigureAwait(false);
            return sb.ToString();
        }

        /// <summary>텍스트를 임베딩 벡터로 변환한다. (RAG 검색에 사용)</summary>
        public async Task<float[]> EmbedAsync(string text, string model = null, CancellationToken ct = default)
        {
            var request = new EmbedRequestDto
            {
                // 빈 문자열/공백도 옵션 기본값으로 폴백(호출측이 "" 를 넘겨도 안전).
                Model = string.IsNullOrWhiteSpace(model) ? _options.EmbeddingModel : model,
                Prompt = text ?? string.Empty
            };
            // 구형 GPU 임베딩 크래시 회피: EmbedNumGpu 지정 시 num_gpu 옵션 부여(0=CPU).
            if (_options.EmbedNumGpu.HasValue)
                request.Options = new OllamaRequestOptionsDto { NumGpu = _options.EmbedNumGpu.Value };
            var payload = JsonHelper.Serialize(request);

            using (var resp = await _http.PostAsync("/api/embeddings",
                new StringContent(payload, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false))
            {
                await EnsureSuccessOrThrowAsync(resp).ConfigureAwait(false);
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var dto = JsonHelper.Deserialize<EmbedResponseDto>(json);
                if (dto?.Embedding == null) return new float[0];
                var result = new float[dto.Embedding.Length];
                for (int i = 0; i < dto.Embedding.Length; i++)
                    result[i] = (float)dto.Embedding[i];
                return result;
            }
        }

        // 성공이 아니면 응답 본문에서 Ollama 오류 메시지를 뽑아 예외로 던진다.
        // (그냥 EnsureSuccessStatusCode 를 쓰면 "400" 같은 상태 코드만 보여 원인 파악이 어렵다.)
        private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage resp)
        {
            if (resp.IsSuccessStatusCode) return;

            string body = "";
            try { body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); }
            catch { /* 본문 읽기 실패는 무시하고 상태 코드만 알린다 */ }

            string detail = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try { detail = JsonHelper.Deserialize<ErrorResponseDto>(body)?.Error; }
                catch { /* JSON 이 아니면 본문 원문을 쓴다 */ }
                if (string.IsNullOrWhiteSpace(detail)) detail = body.Trim();
            }

            throw new HttpRequestException(
                $"Ollama 요청 실패 ({(int)resp.StatusCode} {resp.StatusCode})" +
                (string.IsNullOrWhiteSpace(detail) ? "" : $": {detail}"));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
        }
    }
}
