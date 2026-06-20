using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Upcomputer.Core.Models
{
    /// <summary>
    /// 解析后的协议帧
    /// <para>表示一帧经过校验的完整通信数据，包含命令码、方向、有效载荷和原始字节。</para>
    /// </summary>
    public class ParsedFrame
    {
        /// <summary>命令码字节（参见 <see cref="ProtocolCommandCode"/>）</summary>
        public byte Command { get; set; }

        /// <summary>方向标识：0x00 = 上位机→设备，0x01 = 设备→上位机</summary>
        public byte Direction { get; set; }

        /// <summary>帧有效载荷数据（去除命令码和方向码后的净数据）</summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>原始完整帧字节（含帧头、长度、CRC、帧尾）</summary>
        public byte[] RawData { get; set; } = Array.Empty<byte>();

        /// <summary>是否为设备发往上位机的帧（Direction == 0x01）</summary>
        public bool IsFromDevice => Direction == 0x01;

        /// <summary>是否为上位机发往设备的帧（Direction == 0x00）</summary>
        public bool IsToDevice => Direction == 0x00;
    }

    /// <summary>
    /// 协议命令码定义
    /// <para>
    /// 命令码分区规划：
    /// <list type="bullet">
    ///   <item>0x00-0x0F：系统命令（心跳、握手、设备信息、设备发现）</item>
    ///   <item>0x10-0x2F：治疗控制命令（启停治疗、设置模式/强度/时长、急停）</item>
    ///   <item>0x30-0x4F：运动控制命令（步进电机、舵机控制）</item>
    ///   <item>0x50-0x6F：数据上报命令（压力、水肿、电机状态、治疗进度）</item>
    ///   <item>0x80-0x8F：应答码（ACK/NAK）</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class ProtocolCommandCode
    {
        // ---- 系统命令 (0x00-0x0F) ----

        /// <summary>心跳命令 —— 上位机周期性发送以检测链路存活</summary>
        public const byte Heartbeat = 0x01;

        /// <summary>握手/连接建立命令</summary>
        public const byte Handshake = 0x02;

        /// <summary>设备信息查询命令 —— 查询设备名称、固件版本、运行状态</summary>
        public const byte DeviceInfo = 0x03;

        // ---- 控制命令 (0x10-0x2F) ----

        /// <summary>启动治疗 —— 附带模式、强度、时长参数</summary>
        public const byte StartTherapy = 0x10;

        /// <summary>停止治疗</summary>
        public const byte StopTherapy = 0x11;

        /// <summary>紧急停机 —— 立即停止所有运动并锁定设备</summary>
        public const byte EmergencyStop = 0x12;

        /// <summary>设置治疗模式（康复/按摩/自定义）</summary>
        public const byte SetMode = 0x13;

        /// <summary>设置按摩强度（0-100%）</summary>
        public const byte SetIntensity = 0x14;

        /// <summary>设置治疗时长（分钟）</summary>
        public const byte SetDuration = 0x15;

        /// <summary>暂停治疗</summary>
        public const byte PauseTherapy = 0x16;

        /// <summary>继续治疗</summary>
        public const byte ResumeTherapy = 0x17;

        /// <summary>退出急停状态 —— 解除设备锁定，恢复正常待机</summary>
        public const byte ExitEmergencyStop = 0x18;

        // ---- 运动控制 (0x30-0x4F) ----

        /// <summary>步进电机控制（指定电机ID、转速、方向）</summary>
        public const byte StepperControl = 0x30;

        /// <summary>舵机控制（指定舵机ID、目标角度）</summary>
        public const byte ServoControl = 0x31;

        /// <summary>所有舵机归位 —— 将全部舵机回到初始角度</summary>
        public const byte ServoHomeAll = 0x32;

        /// <summary>电机回零 —— 步进电机归零操作</summary>
        public const byte MotorHome = 0x33;

        /// <summary>电机步进 —— 指定方向、步数、速度的精确步进控制</summary>
        public const byte MotorStep = 0x34;

        /// <summary>读取电机当前位置</summary>
        public const byte MotorPosition = 0x35;

        /// <summary>设置时间 —— 上位机发送NTP时间（8字节小端序UTC+8 Unix毫秒），下位机同步内部时钟</summary>
        public const byte SetTime = 0x36;

        // ---- 数据上报 (0x50-0x6F) ----

        /// <summary>压力数据上报 —— 多路传感器的压力值、峰值、阈值和告警标志</summary>
        public const byte PressureData = 0x50;

        /// <summary>水肿/阻抗数据上报 —— 生物阻抗值和水肿百分比</summary>
        public const byte EdemaData = 0x51;

        /// <summary>电机状态上报 —— 步进电机运行状态和舵机角度</summary>
        public const byte MotorStatus = 0x52;

        /// <summary>治疗进度上报 —— 进度百分比、已用/剩余时间</summary>
        public const byte TherapyProgress = 0x53;

        // ---- 响应码 (0x80-0x8F) ----

        /// <summary>成功应答（ACK）</summary>
        public const byte Ack = 0x80;

        /// <summary>失败应答（NAK）</summary>
        public const byte Nak = 0x81;

        // ---- 设备发现 ----

        /// <summary>设备发现请求 —— UDP 广播查询局域网内的康复设备</summary>
        public const byte DeviceDiscovery = 0x04;

        /// <summary>设备通告响应 —— 设备收到发现请求后返回自身信息</summary>
        public const byte DeviceAnnounce = 0x05;
    }

    /// <summary>
    /// 协议帧结构常量
    /// <para>
    /// 自定义通信协议帧格式如下（共 9+N 字节）：
    /// <code>
    /// ┌──────────┬──────────┬──────────┬──────────┬──────────┬──────────┐
    /// │ 帧头(2B) │ 长度(2B) │ 命令(1B) │ 方向(1B) │ 数据(NB) │ CRC(2B)  │ 帧尾(1B) │
    /// ├──────────┼──────────┼──────────┼──────────┼──────────┼──────────┤
    /// │  0xAA55  │ 小端序   │ 见命令表 │ 00=→设备 │ 变长     │ Modbus   │  0x0D    │
    /// │          │          │          │ 01=→主机 │          │ CRC-16   │          │
    /// └──────────┴──────────┴──────────┴──────────┴──────────┴──────────┘
    /// </code>
    /// 长度字段 = 命令(1) + 方向(1) + 数据区长度(N)，即 dataLength = N + 2。
    /// CRC-16 校验范围：从命令字节到数据区末尾。
    /// </para>
    /// </summary>
    public static class ProtocolConstants
    {
        /// <summary>帧头标识：0x55AA（小端存储为 0xAA 0x55）</summary>
        public const ushort FrameHeader = 0x55AA;

        /// <summary>帧尾标识：0x0D（回车符）</summary>
        public const byte FrameTail = 0x0D;

        /// <summary>数据区最大长度（字节），超过此值的帧将被拒绝</summary>
        public const int MaxDataLength = 1024;

        /// <summary>方向标识：上位机发往设备（0x00）</summary>
        public const byte DirectionToDevice = 0x00;

        /// <summary>方向标识：设备发往上位机（0x01）</summary>
        public const byte DirectionFromDevice = 0x01;
    }
}