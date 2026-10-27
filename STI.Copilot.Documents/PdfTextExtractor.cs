using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using STI.Copilot.Core.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace STI.Copilot.Documents
{
    /// <summary>
    /// PdfPig 로 PDF에서 텍스트를 추출한다. (스캔 이미지 PDF는 OCR이 별도로 필요)
    /// 단순히 page.Text 를 쓰면 표의 행·열 구조가 무너지므로, 단어의 좌표(bounding box)를 이용해
    /// 사람이 보는 레이아웃(행/열)을 복원한다: Y로 행을 묶고, X로 정렬하고, 큰 가로 간격은 탭으로 표시.
    /// </summary>
    public class PdfTextExtractor : IDocumentTextExtractor
    {
        public bool CanHandle(string filePath) =>
            !string.IsNullOrEmpty(filePath) &&
            filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        public string Extract(string filePath)
        {
            var sb = new StringBuilder();
            using (var doc = PdfDocument.Open(filePath))
            {
                foreach (var page in doc.GetPages())
                {
                    string pageText = null;
                    try
                    {
                        var words = page.GetWords()
                            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                            .ToList();
                        if (words.Count > 0)
                            pageText = ReconstructLayout(words);
                    }
                    catch
                    {
                        // 단어 추출 실패 시 아래에서 기본 방식으로 폴백
                        pageText = null;
                    }

                    // 좌표 기반 복원이 안 되면(스캔 PDF 등) 기존 방식으로 폴백
                    sb.AppendLine(string.IsNullOrEmpty(pageText) ? page.Text : pageText);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 단어들을 좌표로 정렬해 행/열 레이아웃을 복원한다.
        /// PDF 좌표계는 좌하단이 원점이라 Y가 클수록 페이지 위쪽이다.
        /// </summary>
        private static string ReconstructLayout(IReadOnlyList<Word> words)
        {
            // 1) 위(큰 Y) → 아래 순으로 정렬 후, 같은 행끼리 그룹핑
            var ordered = words.OrderByDescending(w => w.BoundingBox.Bottom).ToList();
            var lines = new List<List<Word>>();

            foreach (var w in ordered)
            {
                double y = w.BoundingBox.Bottom;
                double h = Math.Max(w.BoundingBox.Height, 1);

                var line = lines.Count > 0 ? lines[lines.Count - 1] : null;
                if (line != null)
                {
                    double refY = line[0].BoundingBox.Bottom;
                    double refH = Math.Max(line[0].BoundingBox.Height, 1);
                    // 세로 위치 차이가 글자 높이의 60% 이내면 같은 줄로 본다.
                    double tolerance = Math.Min(refH, h) * 0.6;
                    if (Math.Abs(refY - y) <= tolerance)
                    {
                        line.Add(w);
                        continue;
                    }
                }
                lines.Add(new List<Word> { w });
            }

            // 2) 각 줄: 왼→오 정렬, 가로 간격이 크면 탭(열 구분)으로
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                var cols = line.OrderBy(w => w.BoundingBox.Left).ToList();
                for (int i = 0; i < cols.Count; i++)
                {
                    if (i > 0)
                    {
                        var prev = cols[i - 1];
                        var cur = cols[i];
                        double gap = cur.BoundingBox.Left - prev.BoundingBox.Right;
                        // 한 칸(space) 폭을 글자 높이의 약 30%로 추정
                        double spaceWidth = Math.Max(Math.Max(prev.BoundingBox.Height, cur.BoundingBox.Height) * 0.3, 1);
                        sb.Append(gap > spaceWidth * 3 ? '\t' : ' ');
                    }
                    sb.Append(cols[i].Text);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
