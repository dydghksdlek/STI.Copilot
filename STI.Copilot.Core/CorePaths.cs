using System;

namespace STI.Copilot.Core
{
    /// <summary>
    /// 앱 런타임 데이터의 기본 저장 폴더.
    /// 기본값 <c>C:\STI_Copilot</c> (docling_service 와 같은 위치에 모음).
    /// 환경변수 <c>STI_COPILOT_DATA_DIR</c> 가 있으면 그 경로를 사용한다.
    /// </summary>
    public static class CorePaths
    {
        public const string DefaultDataDir = @"C:\STI_Copilot";

        public static string DataBaseDir
        {
            get
            {
                try
                {
                    var env = Environment.GetEnvironmentVariable("STI_COPILOT_DATA_DIR");
                    if (!string.IsNullOrWhiteSpace(env)) return env;
                }
                catch { /* 환경변수 접근 실패 시 기본값 */ }
                return DefaultDataDir;
            }
        }
    }
}
