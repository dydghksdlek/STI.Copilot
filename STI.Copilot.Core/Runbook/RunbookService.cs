using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using STI.Copilot.Core.Ollama;
using STI.Copilot.Core.Rag;

namespace STI.Copilot.Core.Runbook
{
    /// <summary>
    /// RunBook 로직 계층. 메타 저장소(RunbookStore) + 임베딩(벡터 저장소) + Ollama 를 묶는다.
    /// - 추가 시 제목+내용을 bge-m3 로 임베딩해 벡터에 저장
    /// - 키워드 검색(관리용) / 의미 검색(답변용) / 유사 항목(대체 후보) 제공
    /// - 폐기/대체/삭제
    /// 벡터 저장소는 문서용(kb.bin)과 분리된 파일이라 RunBook만 따로 검색된다.
    /// </summary>
    public class RunbookService
    {
        private readonly RunbookStore _store;
        private readonly InMemoryVectorStore _vectors;
        private readonly OllamaClient _client;
        private readonly RunbookOptions _options;

        public RunbookService(OllamaClient client, RunbookOptions options = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? new RunbookOptions();
            _store = new RunbookStore(_options.StorePath);
            _vectors = new InMemoryVectorStore(_options.VectorStorePath, _options.EmbeddingModel);
        }

        /// <summary>모든 항목(활성+폐기). 관리 화면에서 상태별로 걸러 쓴다.</summary>
        public IReadOnlyList<RunbookEntry> Entries => _store.Entries;

        public int Count => _store.Count;

        /// <summary>저장된 임베딩 모델과 현재 모델이 달라 재인덱싱이 필요한 상태인지.</summary>
        public bool NeedsReindex => _vectors.NeedsReindex;

        /// <summary>디스크에서 메타+벡터를 불러온다. 앱 시작 시 1회.</summary>
        public void Load()
        {
            _store.Load();
            _vectors.Load();
        }

        /// <summary>새 항목 추가 + 임베딩. 코드/모듈/태그는 선택.</summary>
        public async Task<RunbookEntry> AddAsync(
            string title, string category, string content,
            string author = null, string code = null, string module = null, string tags = null,
            CancellationToken ct = default)
        {
            var entry = _store.Add(title, category, content, author, code, module, tags);
            // 임베딩(Ollama)이 실패해도 메타데이터는 저장한다. 검색 인덱스는 나중에 다시 만들 수 있다.
            try { await IndexAsync(entry, ct).ConfigureAwait(false); }
            catch { /* 임베딩 실패 — 항목은 저장되고, 키워드 검색은 가능. 의미 검색만 누락 */ }
            Save();
            return entry;
        }

        /// <summary>내용 수정 + 재임베딩. null 인자는 기존 값 유지("" 이면 비움).</summary>
        public async Task<bool> UpdateAsync(
            string id, string title = null, string category = null, string content = null,
            string code = null, string module = null, string tags = null,
            CancellationToken ct = default)
        {
            if (!_store.Update(id, title, category, content, code, module, tags)) return false;
            var entry = _store.GetById(id);
            _vectors.RemoveByDocument(id);
            // 임베딩 실패해도(예: Ollama 다운) 수정 내용은 저장한다. 의미검색 인덱스만 나중에 재생성.
            try { await IndexAsync(entry, ct).ConfigureAwait(false); }
            catch { /* 임베딩 실패 — 메타 수정은 유지, 키워드 검색은 가능 */ }
            Save();
            return true;
        }

        /// <summary>키워드(부분일치) 검색 — 관리 화면용. 기본은 활성만.</summary>
        public IReadOnlyList<RunbookEntry> KeywordSearch(
            string term, string category = null, bool includeDeprecated = false)
        {
            IEnumerable<RunbookEntry> q = _store.Entries;
            if (!includeDeprecated) q = q.Where(e => e.IsActive);
            if (!string.IsNullOrEmpty(category)) q = q.Where(e => e.Category == category);

            if (!string.IsNullOrWhiteSpace(term))
            {
                var t = term.Trim();
                q = q.Where(e =>
                    Has(e.Title, t) || Has(e.Content, t) ||
                    Has(e.Code, t) || Has(e.Module, t) || Has(e.Tags, t));
            }
            return q.ToList();
        }

        /// <summary>의미(임베딩) 검색 — 답변용. 활성 항목만 반환.</summary>
        public Task<IReadOnlyList<RunbookHit>> SearchAsync(
            string query, int? topK = null, CancellationToken ct = default) =>
            SearchInternalAsync(query, topK ?? _options.TopK, _options.MinScore, ct);

        /// <summary>관련도 임계값 이상인 항목을 관련도순으로 넉넉히 반환(전체 목록 표시용).
        /// max 만큼까지 가져와 목록·개수 계산에 쓴다.</summary>
        public Task<IReadOnlyList<RunbookHit>> SearchManyAsync(
            string query, int max, CancellationToken ct = default) =>
            SearchInternalAsync(query, max, _options.MinScore, ct);

        /// <summary>새 내용과 유사한 기존 활성 항목 — 대체 후보용.</summary>
        public Task<IReadOnlyList<RunbookHit>> FindSimilarAsync(
            string text, int? topK = null, CancellationToken ct = default) =>
            SearchInternalAsync(text, topK ?? _options.SimilarTopK, _options.SimilarMinScore, ct);

        /// <summary>폐기(이력 보존). 답변·검색에서 제외되지만 벡터는 남겨 복원 가능하게 한다.</summary>
        public bool Deprecate(string id)
        {
            var ok = _store.Deprecate(id);
            if (ok) Save();
            return ok;
        }

        public bool Restore(string id)
        {
            var ok = _store.Restore(id);
            if (ok) Save();
            return ok;
        }

        /// <summary>newId 항목이 oldIds 를 대체 → 옛 항목 폐기 + 대체 링크 기록.</summary>
        public void Supersede(string newId, IEnumerable<string> oldIds)
        {
            _store.MarkSuperseded(newId, oldIds);
            Save();
        }

        /// <summary>완전 삭제 — 메타 + 임베딩 함께 제거.</summary>
        public bool Delete(string id)
        {
            var ok = _store.Delete(id);
            _vectors.RemoveByDocument(id);
            if (ok) Save();
            return ok;
        }

        /// <summary>
        /// 모든 항목을 다시 임베딩해 벡터 저장소를 새로 만든다.
        /// 기존 벡터 파일은 타임스탬프를 붙여 백업한 뒤 초기화한다.
        /// onProgress(done, total) 로 진행률을 알린다. 개별 항목 임베딩 실패는 건너뛴다.
        /// 반환값: 성공적으로 임베딩된 항목 수.
        /// </summary>
        public async Task<int> ReindexAllAsync(Action<int, int> onProgress = null, CancellationToken ct = default)
        {
            BackupVectorFile();
            _vectors.Clear();

            var entries = _store.Entries.ToList();
            int ok = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                // UI 스레드 컨텍스트 유지(진행률 콜백을 UI에서 안전하게) — ConfigureAwait 미사용.
                try { await IndexAsync(entries[i], ct); ok++; }
                catch { /* 개별 실패 스킵 */ }
                onProgress?.Invoke(i + 1, entries.Count);
            }

            _vectors.Save();
            return ok;
        }

        /// <summary>기존 벡터 파일을 "...vec.yyyyMMdd_HHmmss.bak" 으로 백업. 파일 없으면 무시. 반환: 백업 경로 또는 null.</summary>
        public string BackupVectorFile()
        {
            var path = _options.VectorStorePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var bak = path + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";
                File.Copy(path, bak, false);
                return bak;
            }
            catch { return null; }
        }

        // ---- 내부 ----

        private async Task IndexAsync(RunbookEntry entry, CancellationToken ct)
        {
            if (entry == null) return;
            var text = EmbedText(entry);
            var emb = await _client.EmbedAsync(text, _options.EmbeddingModel, ct).ConfigureAwait(false);
            VectorMath.NormalizeInPlace(emb);
            _vectors.Add(new DocumentChunk
            {
                DocumentId = entry.Id,
                Index = 0,
                Text = text,
                Embedding = emb
            });
        }

        private async Task<IReadOnlyList<RunbookHit>> SearchInternalAsync(
            string query, int topK, float minScore, CancellationToken ct)
        {
            var result = new List<RunbookHit>();
            if (string.IsNullOrWhiteSpace(query) || _vectors.Count == 0) return result;

            var qv = await _client.EmbedAsync(query, _options.EmbeddingModel, ct).ConfigureAwait(false);
            VectorMath.NormalizeInPlace(qv);

            // 폐기 항목의 벡터도 남아 있으므로 넉넉히 가져와 상태(활성)로 거른다.
            var hits = _vectors.Search(qv, Math.Max(topK * 3, topK));
            foreach (var h in hits)
            {
                if (h.Score < minScore) continue;
                var entry = _store.GetById(h.Chunk.DocumentId);
                if (entry == null || !entry.IsActive) continue;
                result.Add(new RunbookHit { Entry = entry, Score = h.Score });
                if (result.Count >= topK) break;
            }
            return result;
        }

        private static string EmbedText(RunbookEntry e)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(e.Title ?? "");
            if (!string.IsNullOrWhiteSpace(e.Code)) sb.AppendLine("코드: " + e.Code);
            if (!string.IsNullOrWhiteSpace(e.Module)) sb.AppendLine("모듈: " + e.Module);
            if (!string.IsNullOrWhiteSpace(e.Tags)) sb.AppendLine("태그: " + e.Tags);
            sb.Append(e.Content ?? "");
            return sb.ToString().Trim();
        }

        // 부분일치(대소문자 무시). null/빈 값은 매치 없음 → 빈 키는 검색에 영향 없음.
        private static bool Has(string field, string term) =>
            !string.IsNullOrEmpty(field) && field.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

        private void Save()
        {
            _store.Save();
            _vectors.Save();
        }
    }
}
