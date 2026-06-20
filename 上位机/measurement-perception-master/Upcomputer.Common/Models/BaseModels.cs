using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;

namespace Upcomputer.Common.Models
{
    /// <summary>
    /// 可观察对象基类
    /// <para>实现 <see cref="INotifyPropertyChanged"/> 接口，提供属性变更通知机制。
    /// 所有需要数据绑定的 ViewModel 和 Model 均应继承此类。</para>
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        /// <summary>属性变更事件，WPF/MAUI 绑定引擎通过此事件感知数据变化</summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 触发属性变更通知
        /// </summary>
        /// <param name="propertyName">发生变更的属性名称，默认由编译器通过 <see cref="CallerMemberNameAttribute"/> 自动填充</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 设置字段值并触发属性变更通知（仅当值发生变化时）
        /// <para>典型用法：<c>SetField(ref _field, value)</c></para>
        /// </summary>
        /// <typeparam name="T">字段类型</typeparam>
        /// <param name="field">对后备字段的引用</param>
        /// <param name="value">新值</param>
        /// <param name="propertyName">属性名称，默认由编译器自动填充</param>
        /// <returns>若值发生了变化返回 <c>true</c>，否则返回 <c>false</c></returns>
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    /// <summary>
    /// 无参数的 Relay 命令实现
    /// <para>用于 WPF <see cref="ICommand"/> 绑定，将 UI 操作映射到 ViewModel 中的 <see cref="Action"/> 委托。</para>
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        /// <summary>
        /// 创建一个无参数 Relay 命令
        /// </summary>
        /// <param name="execute">命令执行时调用的操作</param>
        /// <param name="canExecute">判断命令是否可执行的回调，为 <c>null</c> 时始终可执行</param>
        /// <exception cref="ArgumentNullException"><paramref name="execute"/> 为 <c>null</c> 时抛出</exception>
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>判断当前命令是否可执行</summary>
        /// <param name="parameter">命令参数（本实现中未使用）</param>
        /// <returns>可执行返回 <c>true</c></returns>
        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        /// <summary>执行命令</summary>
        /// <param name="parameter">命令参数（本实现中未使用）</param>
        public void Execute(object? parameter) => _execute();

        /// <summary>命令可执行状态变更事件</summary>
        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// 手动触发可执行状态变更通知
        /// <para>当外部条件变化导致 <c>CanExecute</c> 结果可能改变时调用。</para>
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 带泛型参数的 Relay 命令实现
    /// <para>用于需要将命令参数（如选中项）传递给执行逻辑的场景。</para>
    /// </summary>
    /// <typeparam name="T">命令参数类型</typeparam>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool>? _canExecute;

        /// <summary>
        /// 创建一个泛型 Relay 命令
        /// </summary>
        /// <param name="execute">命令执行时调用的操作，接收类型为 <typeparamref name="T"/> 的参数</param>
        /// <param name="canExecute">判断命令是否可执行的回调</param>
        /// <exception cref="ArgumentNullException"><paramref name="execute"/> 为 <c>null</c> 时抛出</exception>
        public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>判断当前命令是否可执行</summary>
        /// <param name="parameter">命令参数，将被强制转换为 <typeparamref name="T"/></param>
        /// <returns>可执行返回 <c>true</c></returns>
        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute((T)parameter!);

        /// <summary>执行命令</summary>
        /// <param name="parameter">命令参数，将被强制转换为 <typeparamref name="T"/></param>
        public void Execute(object? parameter) => _execute((T)parameter!);

        /// <summary>命令可执行状态变更事件</summary>
        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// 手动触发可执行状态变更通知
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

}