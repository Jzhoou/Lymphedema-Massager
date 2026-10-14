using System;
using System.Collections.Generic;
using System.Text;
using Upcomputer.Common.Enums;
using Upcomputer.Core.Models;

namespace Upcomputer.Core.Interfaces
{
    /// <summary>
    /// 通信服务统一接口
    /// <para>定义上位机与下位机之间所有通信操作的契约，包括 WiFi（TCP/UDP）和串口两种传输方式。
    /// 所有数据接收通过事件机制向上层分发。</para>
    /// </summary>
    public interface ICommunicationService
    {
        /// <summary>系统连接状态变更事件（WiFi/串口连接状态、延迟、丢包率等）</summary>
        event EventHandler<SystemStatus>? SystemStatusChanged;

        /// <summary>原始字节数据接收事件（每收到一包原始数据即触发）</summary>
        event EventHandler<byte[]>? DataReceived;

        /// <summary>协议帧解析完成事件（经过帧头/帧尾/CRC 校验后的完整帧）</summary>
        event EventHandler<ParsedFrame>? FrameParsed;

        /// <summary>压力数据上报事件（解析后的多路压力传感器数据）</summary>
        event EventHandler<PressureDataReport>? PressureDataReceived;

        /// <summary>水肿/阻抗数据上报事件</summary>
        event EventHandler<EdemaDataReport>? EdemaDataReceived;

        /// <summary>电机状态上报事件（步进电机状态 + 舵机角度）</summary>
        event EventHandler<MotorStatusReport>? MotorStatusReceived;

        /// <summary>治疗进度上报事件（进度百分比、已用/剩余时间）</summary>
        event EventHandler<TherapyProgressReport>? TherapyProgressReceived;

        /// <summary>电机位置上报事件（张大头步进电机多圈编码器累计值）</summary>
        event EventHandler<MotorPositionResponse>? MotorPositionReceived;

        /// <summary>设备通用应答事件（ACK/NAK 响应）</summary>
        event EventHandler<GeneralResponse>? ResponseReceived;

        /// <summary>当前是否处于已连接状态（WiFi 或串口任一通道）</summary>
        bool IsConnected { get; }

        /// <summary>
        /// 通过 WiFi TCP 连接设备
        /// </summary>
        /// <param name="ipAddress">目标设备 IP 地址</param>
        /// <param name="port">TCP 端口号</param>
        /// <returns>连接成功返回 <c>true</c></returns>
        Task<bool> ConnectAsync(string ipAddress, int port);

        /// <summary>
        /// 通过串口连接设备
        /// </summary>
        /// <param name="portName">串口名称（如 COM3）</param>
        /// <param name="baudRate">波特率（如 115200）</param>
        /// <returns>连接成功返回 <c>true</c></returns>
        Task<bool> ConnectSerialAsync(string portName, int baudRate);

        /// <summary>
        /// 断开 WiFi 连接
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// 断开串口连接
        /// </summary>
        Task DisconnectSerialAsync();

        /// <summary>
        /// 向设备发送命令
        /// </summary>
        /// <param name="command">命令字节（参见 <see cref="ProtocolCommandCode"/>）</param>
        /// <param name="data">命令附加数据，可为 <c>null</c></param>
        /// <param name="transport">传输方式（TCP 或 UDP），默认 TCP</param>
        Task SendCommandAsync(byte command, byte[]? data = null, TransportType transport = TransportType.TCP);

    }



    /// <summary>
    /// 连接状态变更事件参数
    /// </summary>
    public class ConnectionStateChangedEventArgs : EventArgs
    {
        /// <summary>是否已连接</summary>
        public bool IsConnected { get; set; }

        /// <summary>状态描述信息</summary>
        public string? Message { get; set; }

        /// <summary>连接类型（WiFi 或串口）</summary>
        public ConnectionType ConnectionType { get; set; }

    }

    /// <summary>
    /// 连接类型枚举
    /// </summary>
    public enum ConnectionType
    {
        /// <summary>WiFi 连接</summary>
        WiFi,
        /// <summary>串口连接</summary>
        Serial
    }

}