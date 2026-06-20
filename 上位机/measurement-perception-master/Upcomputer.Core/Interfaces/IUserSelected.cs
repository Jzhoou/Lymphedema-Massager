using System;
using Upcomputer.Core.Models;

namespace Upcomputer.Core.Interfaces
{
    /// <summary>
    /// 用户选择事件参数
    /// <para>当用户在 UI 中选中某个患者档案时，通过此参数传递用户信息。</para>
    /// </summary>
    public sealed class UserSelectedEventArgs : EventArgs
    {
        /// <summary>
        /// 创建用户选择事件参数
        /// </summary>
        /// <param name="user">被选中的用户档案</param>
        /// <exception cref="ArgumentNullException"><paramref name="user"/> 为 <c>null</c> 时抛出</exception>
        public UserSelectedEventArgs(UserProfile user)
        {
            User = user ?? throw new ArgumentNullException(nameof(user));
        }

        /// <summary>被选中的用户档案</summary>
        public UserProfile User { get; }
    }

    /// <summary>
    /// 用户选择事件总线接口
    /// <para>用于在不同 ViewModel 之间解耦地广播用户选择事件（发布-订阅模式）。</para>
    /// </summary>
    public interface IUserSelected
    {
        /// <summary>用户被选中时触发的事件</summary>
        event EventHandler<UserSelectedEventArgs>? UserSelected;

        /// <summary>
        /// 发布用户选择事件
        /// </summary>
        /// <param name="user">被选中的用户档案</param>
        void Publish(UserProfile user);
    }
}