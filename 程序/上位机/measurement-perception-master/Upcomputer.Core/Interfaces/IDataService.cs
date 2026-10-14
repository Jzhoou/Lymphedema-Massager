using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upcomputer.Core.Enums;
using Upcomputer.Core.Models;

namespace Upcomputer.Core.Interfaces
{
    /// <summary>
    /// 数据服务接口
    /// <para>提供传感器数据采集的启停控制，以及向设备发送控制命令的能力。
    /// 上层 ViewModel 通过此接口接收实时数据推送。</para>
    /// </summary>
    public interface IDataService
    {
        /// <summary>压力数据实时接收事件</summary>
        event EventHandler<PressureData> PressureDataReceived;

        /// <summary>水肿数据实时接收事件</summary>
        event EventHandler<EdemaData> EdemaDataReceived;

        /// <summary>步进电机状态变更事件</summary>
        event EventHandler<StepperMotorStatus> StepperMotorStatusChanged;

        /// <summary>舵机状态变更事件</summary>
        event EventHandler<ServoStatus> ServoStatusChanged;

        /// <summary>系统连接状态变更事件</summary>
        event EventHandler<SystemStatus> SystemStatusChanged;

        /// <summary>治疗会话状态变更事件</summary>
        event EventHandler<TreatmentSession> TreatmentSessionChanged;

        /// <summary>启动数据采集，开始接收设备上报的实时数据</summary>
        Task StartDataCollectionAsync();

        /// <summary>停止数据采集</summary>
        Task StopDataCollectionAsync();

        /// <summary>
        /// 向设备发送控制命令
        /// </summary>
        /// <param name="command">设备命令类型</param>
        /// <param name="parameters">命令参数（具体类型取决于命令类型）</param>
        Task SendCommandAsync(DeviceCommand command, object parameters);
    }

}