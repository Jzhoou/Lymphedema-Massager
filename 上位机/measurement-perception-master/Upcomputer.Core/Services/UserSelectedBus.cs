using System;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;

namespace Upcomputer.Core.Services
{
    /// <summary>
    /// 用户选择事件总线实现
    /// <para>以发布-订阅模式在不同 ViewModel 之间广播用户选择事件。
    /// 通过 DI 容器注册为单例，确保全局共享同一事件通道。</para>
    /// </summary>
    public sealed class UserSelectedBus : IUserSelected
    {
        /// <summary>用户被选中时触发的事件</summary>
        public event EventHandler<UserSelectedEventArgs>? UserSelected;

        /// <summary>
        /// 发布用户选择事件，通知所有订阅者
        /// </summary>
        /// <param name="user">被选中的用户档案</param>
        public void Publish(UserProfile user)
        {
            UserSelected?.Invoke(this, new UserSelectedEventArgs(user));
        }
    }
}