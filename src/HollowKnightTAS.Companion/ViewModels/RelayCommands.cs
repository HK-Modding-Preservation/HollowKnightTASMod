using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace HollowKnightTAS.Companion.ViewModels
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action execute;
        private readonly Func<bool>? canExecute;

        public RelayCommand(
            Action execute,
            Func<bool>? canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute?.Invoke() ?? true;
        }

        public void Execute(object? parameter)
        {
            execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> execute;
        private readonly Func<bool>? canExecute;
        private bool running;

        public AsyncRelayCommand(
            Func<Task> execute,
            Func<bool>? canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;
        public event EventHandler<Exception>? Faulted;

        public bool CanExecute(object? parameter)
        {
            return !running && (canExecute?.Invoke() ?? true);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            running = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            catch (Exception exception)
            {
                Faulted?.Invoke(this, exception);
            }
            finally
            {
                running = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
