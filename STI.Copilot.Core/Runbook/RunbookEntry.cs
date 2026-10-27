using System.Runtime.Serialization;

namespace STI.Copilot.Core.Runbook
{
    /// <summary>
    /// RunBook 항목 하나. 장비 운영 지식(조치·운영·매뉴얼)의 단위.
    /// 임베딩(검색용 벡터)은 여기 두지 않고, 벡터 저장소에 Id 로 연결해 따로 보관한다.
    /// 날짜는 사람이 읽기 쉽도록 문자열(yyyy-MM-dd HH:mm:ss)로 저장한다.
    /// </summary>
    [DataContract]
    public class RunbookEntry
    {
        /// <summary>고유 번호. "rb-000142" 형식. 한 번 발급한 번호는 재사용하지 않는다.</summary>
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        /// <summary>상황 / 제목 (사용자 입력).</summary>
        [DataMember(Name = "title", Order = 1)]
        public string Title { get; set; }

        /// <summary>카테고리: 조치 / 운영 / 매뉴얼 (설정으로 확장 가능하므로 자유 문자열).</summary>
        [DataMember(Name = "category", Order = 2)]
        public string Category { get; set; }

        /// <summary>실제 방법·절차 (사용자 입력).</summary>
        [DataMember(Name = "content", Order = 3)]
        public string Content { get; set; }

        /// <summary>작성자.</summary>
        [DataMember(Name = "author", Order = 4)]
        public string Author { get; set; }

        [DataMember(Name = "createdAt", Order = 5)]
        public string CreatedAt { get; set; }

        [DataMember(Name = "updatedAt", Order = 6)]
        public string UpdatedAt { get; set; }

        /// <summary>상태: active / deprecated. 폐기(deprecated)는 답변·검색에서 제외되지만 이력은 남는다.</summary>
        [DataMember(Name = "status", Order = 7)]
        public string Status { get; set; }

        /// <summary>이 항목이 대체(폐기)시킨 과거 항목들의 Id.</summary>
        [DataMember(Name = "supersedes", Order = 8)]
        public string[] Supersedes { get; set; }

        /// <summary>알람/에러 코드(예: E-12). 선택. 검색·근거의 강력한 키.</summary>
        [DataMember(Name = "code", Order = 9)]
        public string Code { get; set; }

        /// <summary>모듈/장치 ID(예: RF-3). 선택.</summary>
        [DataMember(Name = "module", Order = 10)]
        public string Module { get; set; }

        /// <summary>태그(쉼표 구분 자유 키워드). 선택.</summary>
        [DataMember(Name = "tags", Order = 11)]
        public string Tags { get; set; }

        /// <summary>활성 상태인지(답변·검색 대상인지).</summary>
        public bool IsActive => Status == RunbookStatus.Active;

        /// <summary>코드/모듈/태그 중 하나라도 있으면 true(칩 표시용).</summary>
        public bool HasKeys =>
            !string.IsNullOrWhiteSpace(Code) || !string.IsNullOrWhiteSpace(Module) || !string.IsNullOrWhiteSpace(Tags);
    }

    /// <summary>항목 상태 값. (프로젝트의 ChatRole 과 같은 문자열 상수 방식)</summary>
    public static class RunbookStatus
    {
        public const string Active = "active";
        public const string Deprecated = "deprecated";
    }

    /// <summary>기본 카테고리. Category 자체는 자유 문자열이라 설정으로 확장할 수 있다.</summary>
    public static class RunbookCategory
    {
        public const string Action = "조치";
        public const string Operation = "운영";
        public const string Manual = "매뉴얼";

        public static readonly string[] Defaults = { Action, Operation, Manual };
    }
}
