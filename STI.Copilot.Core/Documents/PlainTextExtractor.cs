using System;
using System.IO;

namespace STI.Copilot.Core.Documents
{
    /// <summary>txt/md/csv/log 등 순수 텍스트 파일 추출기. 외부 의존성 없음.</summary>
    public class PlainTextExtractor : IDocumentTextExtractor
    {
        private static readonly string[] Extensions = { ".txt", ".md", ".markdown", ".csv", ".log" };

        public bool CanHandle(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            var ext = Path.GetExtension(filePath);
            foreach (var e in Extensions)
                if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public string Extract(string filePath) => File.ReadAllText(filePath);
    }
}
