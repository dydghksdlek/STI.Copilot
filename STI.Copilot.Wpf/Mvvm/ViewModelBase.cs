using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace STI.Copilot.Wpf.Mvvm
{
    /// <summary>INotifyPropertyChanged 최소 구현. 외부 MVVM 프레임워크 없이 동작.</summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
