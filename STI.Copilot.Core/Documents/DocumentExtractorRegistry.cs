using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace STI.Copilot.Core.Documents
{
    /// <summary>
    /// 등록된 추출기들 중 파일에 맞는 것을 골라 텍스트를 뽑는다.
    /// 호스트(앱/장비)가 시작 시 필요한 추출기를 Register 한다.
    /// 예) registry.Register(new PlainTextExtractor()); (+ 필요하면 PDF/DOCX 추출기)
    /// </summary>
    public class DocumentExtractorRegistry
    {
        private readonly List<IDocumentTextExtractor> _extractors = new List<IDocumentTextExtractor>();

        public DocumentExtractorRegistry Register(IDocumentTextExtractor extractor)
        {
            if (extractor == null) throw new ArgumentNullException(nameof(extractor));
            _extractors.Add(extractor);
            return this;
        }

        public bool CanHandle(string filePath) => _extractors.Any(e => e.CanHandle(filePath));

        public string Extract(string filePath)
        {
            var extractor = _extractors.FirstOrDefault(e => e.CanHandle(filePath));
            if (extractor == null)
                throw new NotSupportedException("지원하지 않는 파일 형식입니다: " + Path.GetFileName(filePath));
            return extractor.Extract(filePath);
        }

        /// <summary>기본 레지스트리(순수 텍스트만). PDF/DOCX는 호스트에서 추가 등록.</summary>
        public static DocumentExtractorRegistry CreateDefault()
        {
            return new DocumentExtractorRegistry().Register(new PlainTextExtractor());
        }
    }
}
