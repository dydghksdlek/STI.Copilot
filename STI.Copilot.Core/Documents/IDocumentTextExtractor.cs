namespace STI.Copilot.Core.Documents
{
    /// <summary>
    /// 파일에서 순수 텍스트를 뽑는 추출기. 형식별(txt/pdf/docx…) 구현을 레지스트리에 등록해 사용한다.
    /// PDF/DOCX 등 외부 라이브러리가 필요한 구현은 별도 어셈블리(STI.Copilot.Documents)에 둔다.
    /// </summary>
    public interface IDocumentTextExtractor
    {
        /// <summary>이 추출기가 해당 파일을 처리할 수 있는지(보통 확장자로 판단).</summary>
        bool CanHandle(string filePath);

        /// <summary>파일 내용을 순수 텍스트로 반환.</summary>
        string Extract(string filePath);
    }
}
