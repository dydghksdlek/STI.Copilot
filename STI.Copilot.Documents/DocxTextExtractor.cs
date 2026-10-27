using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using STI.Copilot.Core.Documents;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace STI.Copilot.Documents
{
    /// <summary>
    /// DocumentFormat.OpenXml 로 Word(.docx)에서 텍스트를 추출한다.
    /// 문단은 그대로, 표(Table)는 마크다운 표(| ... |)로 변환해 행·열 구조를 보존한다.
    /// (병합 셀은 마크다운 한계로 단순화된다.)
    /// </summary>
    public class DocxTextExtractor : IDocumentTextExtractor
    {
        public bool CanHandle(string filePath) =>
            !string.IsNullOrEmpty(filePath) &&
            filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

        public string Extract(string filePath)
        {
            var sb = new StringBuilder();
            using (var doc = WordprocessingDocument.Open(filePath, false))
            {
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return "";

                // 본문 요소를 문서 순서대로 훑으며 문단/표를 각각 처리.
                foreach (var el in body.Elements())
                {
                    if (el is Paragraph para)
                        sb.AppendLine(para.InnerText);
                    else if (el is Table table)
                        AppendTable(sb, table);
                }
            }
            return sb.ToString();
        }

        // 워드 표 → 마크다운 표. 첫 행을 헤더로 쓴다.
        private static void AppendTable(StringBuilder sb, Table table)
        {
            var rows = table.Elements<TableRow>().ToList();
            if (rows.Count == 0) return;

            var cellsByRow = new List<List<string>>();
            int maxCols = 0;
            foreach (var row in rows)
            {
                var cells = row.Elements<TableCell>().Select(GetCellText).ToList();
                cellsByRow.Add(cells);
                if (cells.Count > maxCols) maxCols = cells.Count;
            }
            if (maxCols == 0) return;

            // 행마다 열 수가 다를 수 있으니 빈 셀로 채워 유효한 표로 만든다.
            foreach (var cells in cellsByRow)
                while (cells.Count < maxCols) cells.Add("");

            sb.AppendLine();
            sb.Append("| ").Append(string.Join(" | ", cellsByRow[0])).AppendLine(" |");
            sb.Append("|").Append(string.Concat(Enumerable.Repeat(" --- |", maxCols))).AppendLine();
            for (int i = 1; i < cellsByRow.Count; i++)
                sb.Append("| ").Append(string.Join(" | ", cellsByRow[i])).AppendLine(" |");
            sb.AppendLine();
        }

        // 셀 텍스트: 셀 안 문단들을 공백으로 잇고, 마크다운 표에 안전하게 정리.
        private static string GetCellText(TableCell cell)
        {
            var paragraphs = cell.Descendants<Paragraph>()
                .Select(p => p.InnerText)
                .Where(t => !string.IsNullOrEmpty(t));
            var text = string.Join(" ", paragraphs);
            if (string.IsNullOrEmpty(text)) text = cell.InnerText ?? "";

            // 파이프는 escape, 줄바꿈/탭은 공백으로(마크다운 표 셀은 한 줄이어야 함).
            return text.Replace("|", "\\|")
                       .Replace("\r", " ").Replace("\n", " ").Replace("\t", " ")
                       .Trim();
        }
    }
}
