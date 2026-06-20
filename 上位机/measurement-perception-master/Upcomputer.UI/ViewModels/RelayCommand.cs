using System;
using System.Windows.Input;

namespace Upcomputer.UI.ViewModels;

/// <summary>
/// Represents a synchronous command that raises WPF command state notifications.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>
    /// Raised when the command availability changes.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The action to run when the command executes.</param>
    /// <param name="canExecute">Optional predicate that indicates whether the command can execute.</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute();

    /// <summary>
    /// Forces WPF to re-evaluate command availability.
    /// </summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// Represents a synchronous type-safe command.
/// </summary>
/// <typeparam name="T">The parameter type.</typeparam>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    /// <summary>
    /// Raised when the command availability changes.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayCommand{T}"/> class.
    /// </summary>
    /// <param name="execute">The action to run when the command executes.</param>
    /// <param name="canExecute">Optional predicate that indicates whether the command can execute.</param>
    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        if (parameter is T value)
        {
            return _canExecute?.Invoke(value) ?? true;
        }

        return parameter is null && default(T) is null && (_canExecute?.Invoke(default!) ?? true);
    }

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            _execute(value);
            return;
        }

        if (parameter is null && default(T) is null)
        {
            _execute(default!);
            return;
        }

        throw new ArgumentException($"Invalid command parameter type. Expected {typeof(T).FullName}.", nameof(parameter));
    }

    /// <summary>
    /// Forces WPF to re-evaluate command availability.
    /// </summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}