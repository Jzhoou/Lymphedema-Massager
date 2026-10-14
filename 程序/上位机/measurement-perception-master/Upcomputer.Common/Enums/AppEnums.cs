using System;
using System.Collections.Generic;
using System.Text;

namespace Upcomputer.Common.Enums
{
    /// <summary>
    /// 连接状态枚举
    /// <para>表示通信通道（WiFi/串口）的当前连接状态。</para>
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>已断开</summary>
        Disconnected,
        /// <summary>正在连接中</summary>
        Connecting,
        /// <summary>已连接</summary>
        Connected,
        /// <summary>连接错误</summary>
        Error
    }

    /// <summary>
    /// 网络传输协议类型枚举
    /// <para>用于 <see cref="Upcomputer.Core.Interfaces.ICommunicationService.SendCommandAsync"/> 指定发送通道。</para>
    /// </summary>
    public enum TransportType
    {
        /// <summary>TCP 传输，用于控制命令等可靠传输场景</summary>
        TCP = 0,
        /// <summary>UDP 传输，用于传感器数据等高频低延迟场景</summary>
        UDP = 1
    }

    /// <summary>
    /// 治疗模式枚举
    /// <para>与下位机协议中的模式字节一一对应，使用 <c>byte</c> 存储以便直接嵌入协议帧。</para>
    /// </summary>
    public enum TherapyMode : byte
    {
        /// <summary>康复模式 —— 标准康复训练流程</summary>
        Rehabilitation = 0x00,
        /// <summary>按摩模式 —— 按摩放松流程</summary>
        Massage = 0x01,
        /// <summary>自定义模式 —— 用户自定义参数组合</summary>
        Custom = 0x02
    }

    /// <summary>
    /// 电机运行状态枚举
    /// </summary>
    public enum MotorState
    {
        /// <summary>已停止</summary>
        Stopped,
        /// <summary>运行中</summary>
        Running,
        /// <summary>故障</summary>
        Error
    }

    /// <summary>
    /// 设备类型枚举
    /// <para>用于标识系统中不同类型的硬件设备。</para>
    /// </summary>
    public enum DeviceType
    {
        /// <summary>步进电机</summary>
        StepperMotor,
        /// <summary>舵机</summary>
        Servo,
        /// <summary>压力传感器</summary>
        PressureSensor,
        /// <summary>生物阻抗传感器</summary>
        BioimpedanceSensor
    }

    /// <summary>
    /// 告警级别枚举
    /// <para>用于统一管理不同严重程度的告警通知。</para>
    /// </summary>
    public enum AlertLevel
    {
        /// <summary>信息提示</summary>
        Info,
        /// <summary>警告</summary>
        Warning,
        /// <summary>错误</summary>
        Error,
        /// <summary>严重/致命</summary>
        Critical
    }

}