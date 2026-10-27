using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace STI.Copilot.Core.Rag
{
    /// <summary>
    /// 긴 텍스트를 검색하기 좋은 크기의 조각으로 나눈다.
    /// 문장 경계로 잘라 의미가 끊기지 않게 하고, 조각 사이를 조금 겹쳐(overlap) 경계 문맥 손실을 막는다.
    /// 한글/영문 모두 문자 기준으로 동작한다.
    /// </summary>
    public class TextChunker
    {
        /// <summary>한 조각의 목표 최대 글자 수.</summary>
        public int MaxChars { get; set; } = 800;

        /// <summary>조각 사이에 겹칠 글자 수.</summary>
        public int Overlap { get; set; } = 150;

        // 문장 끝(마침표/물음표/느낌표/한중일 구두점/줄바꿈) 뒤에서 분할하며 경계 문자는 유지한다.
        private static readonly Regex SentenceSplit = new Regex(@"(?<=[\.!\?。！？\n])", RegexOptions.Compiled);

        public IEnumerable<string> Split(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;

            // 과도한 공백/빈 줄 정리
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            var sb = new StringBuilder();
            foreach (var raw in SentenceSplit.Split(text))
            {
                var seg = raw;
                if (string.IsNullOrEmpty(seg)) continue;

                // 한 문장 자체가 MaxChars보다 크면 강제로 쪼갠다.
                while (seg.Length > MaxChars)
                {
                    if (sb.Length > 0) { yield return sb.ToString().Trim(); sb.Clear(); }
                    yield return seg.Substring(0, MaxChars).Trim();
                    seg = seg.Substring(MaxChars);
                }

                if (sb.Length + seg.Length > MaxChars && sb.Length > 0)
                {
                    var chunk = sb.ToString().Trim();
                    if (chunk.Length > 0) yield return chunk;

                    // 다음 조각이 앞 문맥 일부를 이어받도록 꼬리를 남긴다.
                    var full = sb.ToString();
                    var tail = full.Length > Overlap ? full.Substring(full.Length - Overlap) : full;
                    sb.Clear();
                    sb.Append(tail);
                }
                sb.Append(seg);
            }

            var last = sb.ToString().Trim();
            if (last.Length > 0) yield return last;
        }
    }
}
