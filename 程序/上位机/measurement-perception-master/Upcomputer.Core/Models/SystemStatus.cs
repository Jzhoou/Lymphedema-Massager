using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upcomputer.Common.Models;
using Upcomputer.Common.Enums;


namespace Upcomputer.Core.Models
{
    /// <summary>
    /// 系统连接状态实体
    /// <para>汇总 WiFi 和串口的连接状态、通信延迟、丢包率等关键指标，
    /// 由通信层实时更新并推送到 UI 层用于状态栏显示。</para>
    /// </summary>
    public class SystemStatus : ObservableObject
    {
        private ConnectionState _wifiState;
        private ConnectionState _serialState;
        private double _tcpLatency;
        private double _udpLatency;
        private double _serialLatency;
        private double _packetLoss;
        private bool _isDeviceConnected;
        private bool _isSerialTransport;
        private int _udpPacketsReceived;
        private int _udpPacketsDropped;
        private string _lastLog = string.Empty;

        /// <summary>WiFi 连接状态</summary>
        public ConnectionState WifiState
        {
            get => _wifiState;
            set => SetField(ref _wifiState, value);
        }

        /// <summary>串口连接状态</summary>
        public ConnectionState SerialState
        {
            get => _serialState;
            set => SetField(ref _serialState, value);
        }

        /// <summary>综合延迟（兼容属性，指向 TCP 延迟）</summary>
        public double Latency
        {
            get => _tcpLatency;
            set => SetField(ref _tcpLatency, value);
        }

        /// <summary>TCP 通道延迟（毫秒），通过心跳 RTT 计算</summary>
        public double TcpLatency
        {
            get => _tcpLatency;
            set => SetField(ref _tcpLatency, value);
        }

        /// <summary>UDP 通道延迟（毫秒），通过设备端时间戳计算</summary>
        public double UdpLatency
        {
            get => _udpLatency;
            set => SetField(ref _udpLatency, value);
        }

        /// <summary>串口通道延迟（毫秒），通过串口心跳 RTT 计算</summary>
        public double SerialLatency
        {
            get => _serialLatency;
            set => SetField(ref _serialLatency, value);
        }

        /// <summary>当前数据是否通过串口通道传输</summary>
        public bool IsSerialTransport
        {
            get => _isSerialTransport;
            set => SetField(ref _isSerialTransport, value);
        }

        /// <summary>丢包率（百分比）</summary>
        public double PacketLoss
        {
            get => _packetLoss;
            set => SetField(ref _packetLoss, value);
        }

        /// <summary>设备是否在线（通过心跳机制判断）</summary>
        public bool IsDeviceConnected
        {
            get => _isDeviceConnected;
            set => SetField(ref _isDeviceConnected, value);
        }

        /// <summary>累计收到的 UDP 数据包数量</summary>
        public int UdpPacketsReceived
        {
            get => _udpPacketsReceived;
            set => SetField(ref _udpPacketsReceived, value);
        }

        /// <summary>累计丢失的 UDP 数据包数量</summary>
        public int UdpPacketsDropped
        {
            get => _udpPacketsDropped;
            set => SetField(ref _udpPacketsDropped, value);
        }

        /// <summary>最新一条状态日志文本（显示在状态栏）</summary>
        public string LastLog
        {
            get => _lastLog;
            set => SetField(ref _lastLog, value);
        }

        /// <summary>综合延迟显示文本（别名）</summary>
        public string LatencyDisplay => TcpLatencyDisplay;

        /// <summary>TCP 延迟格式化显示（如 "TCP: 12ms"）</summary>
        public string TcpLatencyDisplay => $"TCP: {TcpLatency:F0}ms";

        /// <summary>UDP 延迟格式化显示，无数据时显示 "UDP: --"</summary>
        public string UdpLatencyDisplay => UdpLatency > 0 ? $"UDP: {UdpLatency:F0}ms" : "UDP: --";

        /// <summary>串口延迟格式化显示，无数据时显示 "串口: --"</summary>
        public string SerialLatencyDisplay => SerialLatency > 0 ? $"串口: {SerialLatency:F0}ms" : "串口: --";

        /// <summary>丢包率格式化显示（如 "丢包: 0.50%"）</summary>
        public string PacketLossDisplay => $"丢包: {PacketLoss:F2}%";

        /// <summary>综合连接状态显示文本（"已连接" / "未连接"）</summary>
        public string ConnectionStateDisplay => IsDeviceConnected ? "已连接" : "未连接";

        /// <summary>
        /// WiFi 状态指示颜色字符串
        /// <para>返回颜色名称字符串（Green/Yellow/Red/Gray），用于 XAML 中通过绑定动态设置状态指示灯颜色。
        /// 使用字符串而非 Brush 类型，避免 Common 层引用 PresentationCore 程序集。</para>
        /// </summary>
        public string WifiStateColor => WifiState switch
        {
            ConnectionState.Connected => "Green",
            ConnectionState.Connecting => "Yellow",
            ConnectionState.Disconnected => "Red",
            _ => "Gray"
        };
        /*
        public System.Windows.Media.Brush WifiStateColor => WifiState switch
        {
            ConnectionState.Connected => System.Windows.Media.Brushes.Green,
            ConnectionState.Connecting => System.Windows.Media.Brushes.Yellow,
            ConnectionState.Disconnected => System.Windows.Media.Brushes.Red,
            _ => System.Windows.Media.Brushes.Gray
        };
        */
    }
}