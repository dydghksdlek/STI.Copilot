using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using STI.Copilot.Core.Documents;

namespace STI.Copilot.Documents
{
    /// <summary>
    /// Docling(파이썬)으로 PDF를 마크다운(표 구조 보존)으로 추출한다.
    /// 자식 프로세스로 docling_extract.py 를 실행하고 결과 마크다운을 읽어온다.
    /// 파이썬/Docling 이 없거나 실패하면 폴백 추출기(좌표 기반 PdfPig)로 대체해 앱이 멈추지 않게 한다.
    /// </summary>
    public class DoclingExtractor : IDocumentTextExtractor
    {
        private readonly IDocumentTextExtractor _fallback;
        private readonly ExtractionOptions _options;
        private readonly string _pythonExe;
        private readonly string _scriptPath;
        private readonly int _timeoutMs;

        /// <param name="fallback">Docling 실패 시 사용할 추출기(예: PdfTextExtractor).</param>
        /// <param name="options">추출 옵션(OCR 등). 공유 인스턴스로 주입하면 UI 토글이 즉시 반영된다.</param>
        /// <param name="pythonExe">venv 파이썬 실행 파일 경로. null 이면 자동 탐색.</param>
        /// <param name="scriptPath">docling_extract.py 경로. null 이면 자동 탐색.</param>
        /// <param name="timeoutMs">변환 제한 시간(ms). 기본 5분.</param>
        public DoclingExtractor(
            IDocumentTextExtractor fallback = null,
            ExtractionOptions options = null,
            string pythonExe = null,
            string scriptPath = null,
            int timeoutMs = 300000)
        {
            _fallback = fallback;
            _options = options;
            _timeoutMs = timeoutMs;

            var serviceDir = ResolveServiceDir();
            _scriptPath = scriptPath ??
                (serviceDir != null ? Path.Combine(serviceDir, "docling_extract.py") : null);
            _pythonExe = pythonExe ??
                (serviceDir != null ? Path.Combine(serviceDir, ".venv", "Scripts", "python.exe") : null);
        }

        public bool CanHandle(string filePath) =>
            !string.IsNullOrEmpty(filePath) &&
            filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        public string Extract(string filePath)
        {
            // Docling 사용 불가(스크립트/파이썬 없음)면 곧바로 폴백.
            if (!IsAvailable())
                return Fallback(filePath, "Docling 미설치/경로 없음");

            string outFile = null;
            try
            {
                outFile = Path.Combine(Path.GetTempPath(),
                    "docling_" + Guid.NewGuid().ToString("N") + ".md");

                var psi = new ProcessStartInfo
                {
                    FileName = _pythonExe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8,
                    WorkingDirectory = Path.GetDirectoryName(_scriptPath)
                };
                // 인자: 스크립트, 입력, 출력 (+OCR 옵션) — 경로에 공백이 있으므로 각각 따옴표로 감싼다.
                var args = Quote(_scriptPath) + " " + Quote(filePath) + " " + Quote(outFile);
                if (_options != null && _options.UseOcr) args += " --ocr";
                psi.Arguments = args;
                // 한글 인코딩 안전장치
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                using (var proc = new Process { StartInfo = psi })
                {
                    var err = new StringBuilder();
                    proc.ErrorDataReceived += (s, e) => { if (e.Data != null) err.AppendLine(e.Data); };
                    proc.OutputDataReceived += (s, e) => { /* 스크립트는 파일로 출력하므로 stdout 은 무시 */ };

                    proc.Start();
                    proc.BeginErrorReadLine();
                    proc.BeginOutputReadLine();

                    if (!proc.WaitForExit(_timeoutMs))
                    {
                        try { proc.Kill(); } catch { }
                        return Fallback(filePath, "Docling 시간 초과");
                    }

                    if (proc.ExitCode != 0)
                        return Fallback(filePath, "Docling 오류(exit " + proc.ExitCode + "): " + err);
                }

                if (!File.Exists(outFile))
                    return Fallback(filePath, "Docling 출력 파일 없음");

                var md = File.ReadAllText(outFile, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(md))
                    return Fallback(filePath, "Docling 결과 비어있음");

                return md;
            }
            catch (Exception ex)
            {
                return Fallback(filePath, "Docling 예외: " + ex.Message);
            }
            finally
            {
                if (outFile != null)
                {
                    try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }
                }
            }
        }

        // 공백 포함 경로를 안전하게 넘기기 위한 따옴표 처리.
        private static string Quote(string s) => "\"" + s + "\"";

        private bool IsAvailable() =>
            !string.IsNullOrEmpty(_pythonExe) && File.Exists(_pythonExe) &&
            !string.IsNullOrEmpty(_scriptPath) && File.Exists(_scriptPath);

        private string Fallback(string filePath, string reason)
        {
            if (_fallback == null)
                throw new NotSupportedException("Docling 사용 불가하고 폴백 추출기도 없습니다: " + reason);
            Debug.WriteLine("[DoclingExtractor] 폴백 사용 — " + reason);
            return _fallback.Extract(filePath);
        }

        // docling_service(파이썬 venv) 폴더를 우선순위로 찾는다.
        // 1) 환경변수 STI_COPILOT_DOCLING_DIR  2) 고정 위치  3) (개발) exe 상위 python\docling_service
        private static string ResolveServiceDir()
        {
            // 1) 환경변수로 명시 지정된 경로(배포 시 유연)
            try
            {
                var env = Environment.GetEnvironmentVariable("STI_COPILOT_DOCLING_DIR");
                if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
            }
            catch { }

            // 2) 고정 위치 후보(소스 트리 밖으로 뺀 venv)
            string[] fixedCandidates =
            {
                @"C:\STI_Copilot\docling_service",
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "STI.Copilot", "docling_service"),
            };
            foreach (var c in fixedCandidates)
            {
                try { if (Directory.Exists(c)) return c; } catch { }
            }

            // 3) (개발 편의) exe 위치에서 위로 올라가며 python\docling_service 탐색
            try
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    var candidate = Path.Combine(dir.FullName, "python", "docling_service");
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }
    }
}
