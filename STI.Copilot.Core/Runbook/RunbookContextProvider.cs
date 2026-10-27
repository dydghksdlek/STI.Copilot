using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using STI.Copilot.Core.Chat;

namespace STI.Copilot.Core.Runbook
{
    /// <summary>
    /// 질문을 의미검색(bge-m3)해 관련 런북 항목을 근거로 만든다.
    /// 답변 구성(B 방식):
    ///  - 전체 목록: 관련도 임계값 이상인 항목을 코드가 직접 나열(LLM 미경유 → 누락·환각 없음).
    ///    상한(ListCap)을 넘으면 "…외 N건" + 카테고리 요약 + 좁히기 안내로 처리한다.
    ///  - 상세: 상위 DetailCount 개의 실제 내용만 LLM 에 넘겨 풀어쓰게 한다(프롬프트 작게 유지).
    /// LastListText(목록 텍스트)는 채팅 답변 앞에 그대로 붙고, GetContextAsync 반환값(상세)만 LLM 이 받는다.
    /// </summary>
    public class RunbookContextProvider : IChatContextProvider
    {
        private readonly RunbookService _service;

        public RunbookContextProvider(RunbookService service)
        {
            _service = service;
        }

        /// <summary>LLM 이 자세히 풀어쓸 상위 항목 수(상세).</summary>
        public int DetailCount { get; set; } = 3;

        /// <summary>목록에 나열할 최대 항목 수. 초과분은 "…외 N건"으로 요약한다.</summary>
        public int ListCap { get; set; } = 30;

        /// <summary>목록·개수 계산 시 고려할 최대 후보 수(과도한 스캔 방지용 안전 상한).</summary>
        public int MaxConsider { get; set; } = 1000;

        /// <summary>목록에 넣을 "관련" 판정 절대 하한 점수. bge-m3 는 기저 유사도가 높아
        /// 서비스 기본 MinScore(넓게 후보 수집용)보다 엄격하게 잡는다.</summary>
        public float ListMinScore { get; set; } = 0.50f;

        /// <summary>상대 컷오프 비율. 최고 점수 × 이 값 이상만 "관련"으로 본다(질문별 자동 조절).
        /// 절대 하한(ListMinScore)과 함께 큰 쪽을 컷오프로 사용한다.</summary>
        public double RelativeRatio { get; set; } = 0.78;

        /// <summary>직전 검색에서 상세로 다룬 런북 항목(출처 표시용).</summary>
        public IReadOnlyList<RunbookHit> LastHits { get; private set; } = new RunbookHit[0];

        /// <summary>직전 검색의 "전체 목록" 텍스트. 채팅 답변 맨 앞에 그대로 출력된다(LLM 미경유).</summary>
        public string LastListText { get; private set; } = "";

        public async Task<string> GetContextAsync(string userMessage, CancellationToken ct = default)
        {
            LastHits = new RunbookHit[0];
            LastListText = "";
            if (string.IsNullOrWhiteSpace(userMessage)) return "";

            // 넓게 후보를 모은 뒤(서비스 MinScore), "관련" 컷오프로 좁힌다.
            var candidates = await _service.SearchManyAsync(userMessage, MaxConsider, ct).ConfigureAwait(false);
            if (candidates == null || candidates.Count == 0) return "";

            // 컷오프 = max(절대 하한, 최고점수 × 상대비율) → 질문마다 관련 무리만 자동 선택
            float cutoff = System.Math.Max(ListMinScore, (float)(candidates[0].Score * RelativeRatio));
            var all = candidates.Where(h => h.Score >= cutoff).ToList();
            if (all.Count == 0) all.Add(candidates[0]); // 안전장치: 최소 최상위 1건은 포함

            int detailN = System.Math.Min(DetailCount, all.Count);
            var detail = all.Take(detailN).ToList();
            LastHits = detail;
            LastListText = BuildListText(all, detailN);

            // LLM 에는 상세 대상 항목의 실제 내용만 넘긴다(목록은 코드가 이미 출력).
            var sb = new StringBuilder();
            foreach (var h in detail)
            {
                var e = h.Entry;
                sb.Append("── ").Append(e.Id).Append(" (").Append(e.Category).Append(") ")
                  .Append(e.Title).AppendLine(" ──");
                sb.AppendLine(e.Content);
            }
            return sb.ToString().Trim();
        }

        // 채팅 답변 앞에 붙는 결정적(비-LLM) 목록 블록을 만든다.
        private string BuildListText(IReadOnlyList<RunbookHit> all, int detailN)
        {
            int total = all.Count;
            int show = System.Math.Min(total, ListCap);

            var sb = new StringBuilder();
            sb.Append("관련 런북 ").Append(total).AppendLine("건을 찾았습니다.").AppendLine();
            sb.AppendLine("📋 목록");
            for (int i = 0; i < show; i++)
            {
                var e = all[i].Entry;
                sb.Append("- `").Append(e.Id).Append("` ").Append(e.Title);
                if (!string.IsNullOrWhiteSpace(e.Code)) sb.Append(" · ").Append(e.Code);
                if (!string.IsNullOrWhiteSpace(e.Module)) sb.Append(" · ").Append(e.Module);
                sb.AppendLine();
            }

            if (total > show)
            {
                int rest = total - show;
                sb.Append("…외 ").Append(rest).Append("건 (").Append(CategorySummary(all)).AppendLine(")");
                sb.AppendLine("→ 특정 장비·카테고리로 좁혀 물어보시거나, RunBook 목록 화면에서 검색해 전체를 확인하세요.");
            }

            sb.AppendLine().Append("🔍 주요 내용 (상위 ").Append(detailN).AppendLine("건)").AppendLine();
            return sb.ToString();
        }

        // "조치 38 · 운영 6 · 매뉴얼 3" 형태의 카테고리별 건수 요약(초과 시 좁히기 판단용).
        private static string CategorySummary(IReadOnlyList<RunbookHit> all)
        {
            var groups = all.GroupBy(h => string.IsNullOrWhiteSpace(h.Entry.Category) ? "기타" : h.Entry.Category)
                            .OrderByDescending(g => g.Count())
                            .Select(g => g.Key + " " + g.Count());
            return string.Join(" · ", groups);
        }
    }
}
