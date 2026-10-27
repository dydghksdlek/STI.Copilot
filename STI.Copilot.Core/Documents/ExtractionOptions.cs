namespace STI.Copilot.Core.Documents
{
    /// <summary>
    /// 문서 추출 런타임 옵션. UI 토글 등으로 값이 바뀔 수 있어, 생성 시점 값이 아니라
    /// 추출 시점 값을 읽도록 "공유 인스턴스"로 참조한다(호스트가 한 개를 만들어 추출기와 VM 양쪽에 주입).
    /// </summary>
    public class ExtractionOptions
    {
        /// <summary>
        /// PDF에서 OCR(이미지 글자 인식)을 수행할지. 기본 false.
        /// 텍스트 PDF는 꺼야 빠르고, 스캔(이미지) PDF만 켠다.
        /// </summary>
        public bool UseOcr { get; set; }
    }
}
