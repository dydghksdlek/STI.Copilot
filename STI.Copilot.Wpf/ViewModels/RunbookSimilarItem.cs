using STI.Copilot.Core.Runbook;
using STI.Copilot.Wpf.Mvvm;

namespace STI.Copilot.Wpf.ViewModels
{
    /// <summary>
    /// 항목 입력 시 제시되는 "비슷한 기존 항목"(대체 후보) 한 건.
    /// 체크하면 저장 시 폐기(supersede) 대상이 된다.
    /// </summary>
    public class RunbookSimilarItem : ViewModelBase
    {
        private bool _isChecked;

        public RunbookSimilarItem(RunbookEntry entry, float score)
        {
            Entry = entry;
            Score = score;
        }

        public RunbookEntry Entry { get; }
        public float Score { get; }

        /// <summary>유사도 표시("87%").</summary>
        public string ScoreText => ((int)(Score * 100 + 0.5f)).ToString() + "%";

        /// <summary>폐기 대상으로 선택됨.</summary>
        public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
    }
}
