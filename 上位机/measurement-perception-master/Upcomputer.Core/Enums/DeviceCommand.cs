using System;
using System.Collections.Generic;
using System.Text;

namespace Upcomputer.Core.Enums
{
    /// <summary>
    /// 设备控制命令枚举
    /// <para>用于 <see cref="Upcomputer.Core.Interfaces.IDataService.SendCommandAsync"/> 向下位机发送连接管理类命令。</para>
    /// </summary>
    public enum DeviceCommand
    {
        /// <summary>建立连接</summary>
        Connect,
        /// <summary>断开连接</summary>
        Disconnect
    }
}