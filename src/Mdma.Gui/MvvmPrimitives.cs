using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Mdma.Gui;

/// <summary>
/// Minimal hand-rolled MVVM base object for property change notifications.
/// Zero third-party dependencies.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null
    )
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Minimal hand-rolled RelayCommand for MVVM command binding.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Strongly-typed RelayCommand for MVVM command binding with parameters.
/// </summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Predicate<T?>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        if (parameter is T val)
            return _canExecute?.Invoke(val) ?? true;
        if (parameter is null && default(T) is null)
            return _canExecute?.Invoke(default) ?? true;
        return _canExecute?.Invoke(default) ?? true;
    }

    public void Execute(object? parameter)
    {
        if (parameter is T val)
            _execute(val);
        else if (parameter is null)
            _execute(default);
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
