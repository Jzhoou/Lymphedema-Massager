using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Upcomputer.Common.Enums;


namespace Upcomputer.Core.Models
{
    #region 请求数据格式（上位机 → 下位机）

    /// <summary>
    /// 启动治疗请求数据
    /// <para>包含治疗模式、强度和时长参数，用于构建 <see cref="ProtocolCommandCode.StartTherapy"/> 命令。</para>
    /// </summary>
    public class StartTherapyRequest
    {
        /// <summary>治疗模式（康复/按摩/自定义）</summary>
        public TherapyMode Mode { get; set; }

        /// <summary>按摩强度百分比（0-100%）</summary>
        public byte Intensity { get; set; }

        /// <summary>治疗时长（单位：分钟）</summary>
        public byte Duration { get; set; }
    }

    /// <summary>
    /// 停止治疗请求数据
    /// <para>无附加数据，仅发送 <see cref="ProtocolCommandCode.StopTherapy"/> 命令码。</para>
    /// </summary>
    public class StopTherapyRequest
    {
    }

    /// <summary>
    /// 紧急停机请求数据
    /// <para>无附加数据，仅发送 <see cref="ProtocolCommandCode.EmergencyStop"/> 命令码。</para>
    /// </summary>
    public class EmergencyStopRequest
    {
    }

    /// <summary>
    /// 设置治疗模式请求数据
    /// </summary>
    public class SetModeRequest
    {
        /// <summary>目标治疗模式</summary>
        public TherapyMode Mode { get; set; }
    }

    /// <summary>
    /// 设置按摩强度请求数据
    /// </summary>
    public class SetIntensityRequest
    {
        /// <summary>强度百分比（0-100%）</summary>
        public byte Intensity { get; set; }
    }

    /// <summary>
    /// 设置治疗时长请求数据
    /// </summary>
    public class SetDurationRequest
    {
        /// <summary>治疗时长（单位：分钟）</summary>
        public byte Duration { get; set; }
    }

    /// <summary>
    /// 步进电机控制请求数据
    /// </summary>
    public class StepperControlRequest
    {
        /// <summary>电机编号（1 或 2）</summary>
        public byte MotorId { get; set; }

        /// <summary>转速（单位：rpm）</summary>
        public ushort Speed { get; set; }

        /// <summary>方向（0=停止, 1=正转, 2=反转）</summary>
        public byte Direction { get; set; }
    }

    /// <summary>
    /// 舵机控制请求数据
    /// </summary>
    public class ServoControlRequest
    {
        /// <summary>舵机编号（1-6）</summary>
        public byte ServoId { get; set; }

        /// <summary>目标角度（0-180度）</summary>
        public byte Angle { get; set; }
    }

    /// <summary>
    /// 所有舵机归位请求数据
    /// </summary>
    public class ServoHomeAllRequest
    {
        // 无附加数据，仅发送 <see cref="ProtocolCommandCode.ServoHomeAll"/> 命令码
    }

    /// <summary>
    /// 电机步进请求数据
    /// 数据7字节:
    /// [0] 方向: 0x00=正转, 0x01=反转
    /// [1-4] 步数: 4字节无符号整型（高字节在前）
    /// [5-6] 速度: 2字节无符号整型（高字节在前，单位RPM）
    /// </summary>
    public class MotorStepRequest
    {
        /// <summary>方向（0x00=正转, 0x01=反转）</summary>
        public byte Direction { get; set; }

        /// <summary>步进总步数（4字节无符号整型）</summary>
        public uint Steps { get; set; }

        /// <summary>转速（单位：RPM，2字节无符号整型）</summary>
        public ushort SpeedRpm { get; set; }
    }

    #endregion

    #region 响应/上报数据格式（下位机 → 上位机）

    /// <summary>
    /// 心跳响应数据
    /// <para>无附加数据，收到此帧即可确认链路存活。</para>
    /// </summary>
    public class HeartbeatResponse
    {
    }

    /// <summary>
    /// 压力数据上报
    /// <para>包含多路压力传感器的实时数据，每路由传感器ID、当前值、峰值、阈值和告警标志组成。</para>
    /// </summary>
    public class PressureDataReport
    {
        /// <summary>多路压力传感器数据列表</summary>
        public List<PressureDataItem> Items { get; set; } = new();

        /// <summary>传感器数量</summary>
        public byte SensorCount => (byte)Items.Count;
    }

    /// <summary>
    /// 单路压力传感器数据
    /// </summary>
    public class PressureDataItem
    {
        /// <summary>传感器编号（1-6）</summary>
        public byte SensorId { get; set; }

        /// <summary>当前压力值（单位：g）</summary>
        public float CurrentValue { get; set; }

        /// <summary>历史最大压力值（单位：g）</summary>
        public float MaxValue { get; set; }

        /// <summary>告警阈值（单位：g）</summary>
        public float Threshold { get; set; }

        /// <summary>告警标志（0=正常, 1=超过阈值）</summary>
        public byte AlertFlag { get; set; }
    }

    /// <summary>
    /// 水肿/阻抗数据上报
    /// </summary>
    public class EdemaDataReport
    {
        /// <summary>传感器编号（1-2）</summary>
        public byte SensorId { get; set; }

        /// <summary>生物阻抗值（单位：Ω）</summary>
        public float Impedance { get; set; }

        /// <summary>水肿百分比（单位：%）</summary>
        public float EdemaPercent { get; set; }

        /// <summary>告警标志（0=正常, 1=超过阈值）</summary>
        public byte AlertFlag { get; set; }
    }

    /// <summary>
    /// 电机状态上报
    /// </summary>
    public class MotorStatusReport
    {
        /// <summary>步进电机1状态（0=停止, 1=运行, 2=错误）</summary>
        public byte Stepper1State { get; set; }

        /// <summary>步进电机2状态（0=停止, 1=运行, 2=错误）</summary>
        public byte Stepper2State { get; set; }

        /// <summary>步进电机1转速（单位：rpm）</summary>
        public ushort Stepper1Speed { get; set; }

        /// <summary>步进电机2转速（单位：rpm）</summary>
        public ushort Stepper2Speed { get; set; }

        /// <summary>6路舵机角度数组（索引0-5对应舵机1-6，单位：度）</summary>
        public byte[] ServoAngles { get; set; } = new byte[6];
    }

    /// <summary>
    /// 治疗进度上报
    /// </summary>
    public class TherapyProgressReport
    {
        /// <summary>治疗进度百分比（0-100%）</summary>
        public byte Progress { get; set; }

        /// <summary>已用时间（单位：秒）</summary>
        public ushort ElapsedTime { get; set; }

        /// <summary>剩余时间（单位：秒）</summary>
        public ushort RemainingTime { get; set; }
    }

    /// <summary>
    /// 电机位置响应
    /// <para>下位机主动上报 2 号电机和 4 号电机的位置，各占 4 字节大端序 int32。</para>
    /// <para>数据格式: [2号电机位置(4)] [4号电机位置(4)]</para>
    /// </summary>
    public class MotorPositionResponse
    {
        /// <summary>2号电机编码器累计值（多圈累积，大端序 int32）</summary>
        public int Motor2EncoderValue { get; set; }

        /// <summary>4号电机编码器累计值（多圈累积，大端序 int32）</summary>
        public int Motor4EncoderValue { get; set; }

        /// <summary>兼容旧代码的单值访问，默认返回 2 号电机位置。</summary>
        public int EncoderValue => Motor2EncoderValue;
    }

    /// <summary>
    /// 通用响应
    /// </summary>
    public class GeneralResponse
    {
        /// <summary>结果码（0=成功, 非0=错误码）</summary>
        public byte Result { get; set; }

        /// <summary>具体错误编码（仅在 Result 非0时有效）</summary>
        public byte ErrorCode { get; set; }
    }

    /// <summary>
    /// 设备信息响应
    /// </summary>
    public class DeviceInfoResponse
    {
        /// <summary>设备名称（最多15字节 UTF-8 字符串）</summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>固件版本号（最多15字节 UTF-8 字符串）</summary>
        public string FirmwareVersion { get; set; } = string.Empty;

        /// <summary>设备运行状态（0=空闲, 1=运行中, 2=故障）</summary>
        public byte DeviceStatus { get; set; }
    }

    /// <summary>
    /// 设备发现通告数据
    /// </summary>
    public class DeviceAnnounceData
    {
        /// <summary>设备 IP 地址（4字节 IPv4 地址）</summary>
        public byte[] IpAddress { get; set; } = new byte[4];

        /// <summary>设备 TCP 监听端口号</summary>
        public ushort TcpPort { get; set; }

        /// <summary>设备名称</summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>固件版本号</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>设备运行状态</summary>
        public byte Status { get; set; }
    }

    #endregion



}
