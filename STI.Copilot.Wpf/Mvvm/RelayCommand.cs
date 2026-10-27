using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace STI.Copilot.Wpf.Mvvm
{
    /// <summary>동기 ICommand 구현.</summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>파라미터(T)를 받는 동기 ICommand. 목록 항목처럼 대상이 있는 동작에 쓴다.</summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(Cast(parameter));
        public void Execute(object parameter) => _execute(Cast(parameter));
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        private static T Cast(object p) => p is T t ? t : default;
    }

    /// <summary>비동기 ICommand 구현. 실행 중에는 중복 실행을 막는다.</summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => !_isRunning && (_canExecute == null || _canExecute());

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;
            _isRunning = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute();
            }
            finally
            {
                _isRunning = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
