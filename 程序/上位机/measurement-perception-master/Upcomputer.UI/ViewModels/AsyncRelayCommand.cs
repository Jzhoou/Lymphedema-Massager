using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Upcomputer.UI.ViewModels;

/// <summary>
/// Represents an asynchronous command that serializes execution and reports state changes to WPF.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly object _syncRoot = new();
    private bool _isExecuting;
    private EventHandler? _canExecuteChanged;

    /// <summary>
    /// Raised when the command availability changes.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add
        {
            _canExecuteChanged += value;
            CommandManager.RequerySuggested += value;
        }
        remove
        {
            _canExecuteChanged -= value;
            CommandManager.RequerySuggested -= value;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous action to run.</param>
    /// <param name="canExecute">Optional predicate that indicates whether the command can execute.</param>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        _ = ExecuteAsync();
    }

    /// <summary>
    /// Manually notifies WPF that the command state may have changed.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            _canExecuteChanged?.Invoke(this, EventArgs.Empty);
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            _canExecuteChanged?.Invoke(this, EventArgs.Empty);
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        dispatcher.Invoke(() =>
        {
            _canExecuteChanged?.Invoke(this, EventArgs.Empty);
            CommandManager.InvalidateRequerySuggested();
        }, System.Windows.Threading.DispatcherPriority.Normal);
    }

    private async Task ExecuteAsync()
    {
        if (!TryStartExecution())
        {
            return;
        }

        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"[AsyncRelayCommand] 执行异常: {ex}");
        }
        finally
        {
            FinishExecution();
        }
    }

    private bool TryStartExecution()
    {
        lock (_syncRoot)
        {
            if (_isExecuting || !(_canExecute?.Invoke() ?? true))
            {
                return false;
            }

            _isExecuting = true;
        }

        RaiseCanExecuteChanged();
        return true;
    }

    private void FinishExecution()
    {
        lock (_syncRoot)
        {
            _isExecuting = false;
        }

        RaiseCanExecuteChanged();
    }
}

/// <summary>
/// Represents a type-safe asynchronous command.
/// </summary>
/// <typeparam name="T">The parameter type.</typeparam>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T, Task> _execute;
    private readonly Func<T, bool>? _canExecute;
    private readonly object _syncRoot = new();
    private bool _isExecuting;
    private EventHandler? _canExecuteChanged;

    /// <summary>
    /// Raised when the command availability changes.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add
        {
            _canExecuteChanged += value;
            CommandManager.RequerySuggested += value;
        }
        remove
        {
            _canExecuteChanged -= value;
            CommandManager.RequerySuggested -= value;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncRelayCommand{T}"/> class.
    /// </summary>
    /// <param name="execute">The asynchronous action to run.</param>
    /// <param name="canExecute">Optional predicate that indicates whether the command can execute.</param>
    public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        if (_isExecuting)
        {
            return false;
        }

        if (!TryGetParameter(parameter, out var typedParameter))
        {
            return false;
        }

        return _canExecute?.Invoke(typedParameter) ?? true;
    }

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        _ = ExecuteAsync(parameter);
    }

    /// <summary>
    /// Manually notifies WPF that the command state may have changed.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            _canExecuteChanged?.Invoke(this, EventArgs.Empty);
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            _canExecuteChanged?.Invoke(this, EventArgs.Empty);
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        dispatcher.Invoke(() =>
        {
            _canExecuteChanged?.Invoke(this, EventArgs.Empty);
            CommandManager.InvalidateRequerySuggested();
        }, System.Windows.Threading.DispatcherPriority.Normal);
    }

    private async Task ExecuteAsync(object? parameter)
    {
        if (!TryStartExecution() || !TryGetParameter(parameter, out var typedParameter))
        {
            return;
        }

        try
        {
            await _execute(typedParameter).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"[AsyncRelayCommand] 执行异常: {ex}");
        }
        finally
        {
            FinishExecution();
        }
    }

    private bool TryStartExecution()
    {
        lock (_syncRoot)
        {
            if (_isExecuting)
            {
                return false;
            }

            _isExecuting = true;
        }

        RaiseCanExecuteChanged();
        return true;
    }

    private void FinishExecution()
    {
        lock (_syncRoot)
        {
            _isExecuting = false;
        }

        RaiseCanExecuteChanged();
    }

    private static bool TryGetParameter(object? parameter, out T typedParameter)
    {
        if (parameter is T value)
        {
            typedParameter = value;
            return true;
        }

        if (parameter is null && default(T) is null)
        {
            typedParameter = default!;
            return true;
        }

        typedParameter = default!;
        return false;
    }
}
