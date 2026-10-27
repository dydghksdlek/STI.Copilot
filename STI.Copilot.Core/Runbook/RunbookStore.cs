using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using STI.Copilot.Core.Ollama; // JsonHelper 재사용 (같은 어셈블리 internal)

namespace STI.Copilot.Core.Runbook
{
    /// <summary>
    /// RunBook 항목의 "메타데이터" 저장소. JSON 파일로 영속화하고 메모리에 올려 다룬다.
    /// 임베딩은 별도 벡터 저장소가 담당하며 여기서는 Id 로만 연결된다(2단계에서 연동).
    /// 저장은 임시 파일에 쓴 뒤 교체(atomic)해 쓰다가 중단돼도 손상되지 않게 한다.
    /// 외부 NuGet 의존성 없음 — 프로젝트의 이식성 원칙 유지.
    /// </summary>
    public class RunbookStore
    {
        private const int FormatVersion = 1;

        private readonly string _filePath;
        private readonly List<RunbookEntry> _entries = new List<RunbookEntry>();
        private int _nextSeq = 1; // 다음 발급 순번 (삭제해도 재사용하지 않음)

        public RunbookStore(string filePath)
        {
            _filePath = filePath;
        }

        public IReadOnlyList<RunbookEntry> Entries => _entries;
        public int Count => _entries.Count;

        /// <summary>새 항목 추가. Id·날짜·상태를 자동으로 채운다. 코드/모듈/태그는 선택.</summary>
        public RunbookEntry Add(string title, string category, string content, string author = null,
                                string code = null, string module = null, string tags = null)
        {
            var now = Now();
            var entry = new RunbookEntry
            {
                Id = "rb-" + _nextSeq.ToString("D6"),
                Title = title ?? "",
                Category = category ?? "",
                Content = content ?? "",
                Author = author ?? "",
                CreatedAt = now,
                UpdatedAt = now,
                Status = RunbookStatus.Active,
                Supersedes = new string[0],
                Code = code ?? "",
                Module = module ?? "",
                Tags = tags ?? ""
            };
            _nextSeq++;
            _entries.Add(entry);
            return entry;
        }

        public RunbookEntry GetById(string id) =>
            _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>내용 수정. null 인 인자는 기존 값 유지("" 이면 비움). 수정 시각 갱신.</summary>
        public bool Update(string id, string title = null, string category = null, string content = null,
                           string code = null, string module = null, string tags = null)
        {
            var e = GetById(id);
            if (e == null) return false;
            if (title != null) e.Title = title;
            if (category != null) e.Category = category;
            if (content != null) e.Content = content;
            if (code != null) e.Code = code;
            if (module != null) e.Module = module;
            if (tags != null) e.Tags = tags;
            e.UpdatedAt = Now();
            return true;
        }

        /// <summary>폐기(이력 보존). 답변·검색에서 제외되지만 항목은 남는다.</summary>
        public bool Deprecate(string id) => SetStatus(id, RunbookStatus.Deprecated);

        /// <summary>폐기된 항목을 다시 활성화.</summary>
        public bool Restore(string id) => SetStatus(id, RunbookStatus.Active);

        /// <summary>완전 삭제(이력도 제거).</summary>
        public bool Delete(string id) =>
            _entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;

        /// <summary>newId 항목이 대체한 과거 항목들을 폐기하고, 대체 링크를 기록한다.</summary>
        public void MarkSuperseded(string newId, IEnumerable<string> oldIds)
        {
            var list = oldIds?.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray() ?? new string[0];
            if (list.Length == 0) return;
            foreach (var oldId in list) Deprecate(oldId);
            var e = GetById(newId);
            if (e != null) e.Supersedes = list;
        }

        // ---- 영속화 ----

        public void Save()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var dto = new RunbookFileDto
            {
                Version = FormatVersion,
                NextSeq = _nextSeq,
                Entries = _entries.ToArray()
            };
            var json = JsonHelper.Serialize(dto);

            // 손상 방지: 임시 파일에 먼저 쓰고 원자적으로 교체
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
            else File.Move(tmp, _filePath);
        }

        public void Load()
        {
            _entries.Clear();
            _nextSeq = 1;
            if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath)) return;

            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            var dto = JsonHelper.Deserialize<RunbookFileDto>(json);
            if (dto?.Entries != null) _entries.AddRange(dto.Entries);
            // 이 기능 이전에 저장된 항목은 코드/모듈/태그가 null → ""로 정규화(칩 표시·검색 트리거 일관성).
            foreach (var e in _entries)
            {
                e.Code = e.Code ?? "";
                e.Module = e.Module ?? "";
                e.Tags = e.Tags ?? "";
            }
            _nextSeq = Math.Max(dto?.NextSeq ?? 1, 1);

            // 안전장치: 저장된 순번이 실제 최대 Id 보다 작으면 보정해 재사용을 막는다.
            int maxSeq = _entries.Select(SeqOf).DefaultIfEmpty(0).Max();
            if (_nextSeq <= maxSeq) _nextSeq = maxSeq + 1;
        }

        // ---- 내부 ----

        private bool SetStatus(string id, string status)
        {
            var e = GetById(id);
            if (e == null) return false;
            e.Status = status;
            e.UpdatedAt = Now();
            return true;
        }

        private static int SeqOf(RunbookEntry e)
        {
            if (e?.Id != null && e.Id.StartsWith("rb-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(e.Id.Substring(3), out int n)) return n;
            return 0;
        }

        private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        [DataContract]
        internal class RunbookFileDto
        {
            [DataMember(Name = "version", Order = 0)] public int Version { get; set; }
            [DataMember(Name = "nextSeq", Order = 1)] public int NextSeq { get; set; }
            [DataMember(Name = "entries", Order = 2)] public RunbookEntry[] Entries { get; set; }
        }
    }
}
