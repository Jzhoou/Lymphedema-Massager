using Microsoft.Extensions.Logging;
using Upcomputer.Common.Enums;
using Upcomputer.Common.Logger;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;

namespace Upcomputer.Communication
{
    /// <summary>
    /// 通信模式枚举
    /// <para>标识当前活跃的通信通道类型。</para>
    /// </summary>
    public enum CommunicationMode
    {
        /// <summary>无活跃通道</summary>
        None,
        /// <summary>WiFi（TCP/UDP）通道</summary>
        WiFi,
        /// <summary>串口通道</summary>
        Serial
    }

    /// <summary>
    /// 通信服务路由器
    /// <para>
    /// 实现 <see cref="ICommunicationService"/> 接口，在 WiFi 和串口之间自动路由。
    /// 同一时刻仅允许一种通信模式处于活跃状态，切换模式时自动断开另一种。
    /// 内部通过事件转发机制，确保只有当前活跃通道的事件被传递给上层。
    /// </para>
    /// </summary>
    public sealed class CommunicationServiceRouter : ICommunicationService
    {
        private readonly WifiCommunication _wifiCommunication;
        private readonly SerialCommunication _serialCommunication;
        private readonly ILogger<CommunicationServiceRouter> _logger;
        private CommunicationMode _currentMode = CommunicationMode.None;

        /// <summary>
        /// 创建通信服务路由器
        /// </summary>
        /// <param name="wifiCommunication">WiFi 通信服务实例</param>
        /// <param name="serialCommunication">串口通信服务实例</param>
        public CommunicationServiceRouter(WifiCommunication wifiCommunication, SerialCommunication serialCommunication)
        {
            _wifiCommunication = wifiCommunication;
            _serialCommunication = serialCommunication;
            _logger = AppLogger.CreateLogger<CommunicationServiceRouter>();

            _wifiCommunication.SystemStatusChanged += OnInnerSystemStatusChanged;
            _serialCommunication.SystemStatusChanged += OnInnerSystemStatusChanged;
            _wifiCommunication.DataReceived += ForwardDataReceived;
            _wifiCommunication.FrameParsed += ForwardFrameParsed;
            _wifiCommunication.PressureDataReceived += ForwardPressureDataReceived;
            _wifiCommunication.EdemaDataReceived += ForwardEdemaDataReceived;
            _wifiCommunication.MotorStatusReceived += ForwardMotorStatusReceived;
            _wifiCommunication.TherapyProgressReceived += ForwardTherapyProgressReceived;
            _wifiCommunication.MotorPositionReceived += ForwardMotorPositionReceived;
            _wifiCommunication.ResponseReceived += ForwardResponseReceived;

            _serialCommunication.DataReceived += ForwardDataReceived;
            _serialCommunication.FrameParsed += ForwardFrameParsed;
            _serialCommunication.PressureDataReceived += ForwardPressureDataReceived;
            _serialCommunication.EdemaDataReceived += ForwardEdemaDataReceived;
            _serialCommunication.MotorStatusReceived += ForwardMotorStatusReceived;
            _serialCommunication.TherapyProgressReceived += ForwardTherapyProgressReceived;
            _serialCommunication.MotorPositionReceived += ForwardMotorPositionReceived;
            _serialCommunication.ResponseReceived += ForwardResponseReceived;
        }

        public event EventHandler<SystemStatus>? SystemStatusChanged;
        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<ParsedFrame>? FrameParsed;
        public event EventHandler<PressureDataReport>? PressureDataReceived;
        public event EventHandler<EdemaDataReport>? EdemaDataReceived;
        public event EventHandler<MotorStatusReport>? MotorStatusReceived;
        public event EventHandler<TherapyProgressReport>? TherapyProgressReceived;
        public event EventHandler<MotorPositionResponse>? MotorPositionReceived;
        public event EventHandler<GeneralResponse>? ResponseReceived;

        public bool IsConnected => _wifiCommunication.IsConnected || _serialCommunication.IsConnected;

        public CommunicationMode CurrentMode => _currentMode;

        /// <summary>
        /// 判断指定串口是否已知为"幽灵端口"（注册表仍有记录但底层句柄已失效）
        /// </summary>
        public bool IsSerialPortLikelyStale(string portName) => _serialCommunication.IsPortLikelyStale(portName);

        /// <summary>
        /// 通过 WiFi TCP 连接设备（自动断开串口）
        /// </summary>
        /// <param name="ipAddress">目标设备 IP 地址</param>
        /// <param name="port">TCP 端口号</param>
        /// <returns>连接成功返回 <c>true</c></returns>
        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            _currentMode = CommunicationMode.WiFi;
            await _serialCommunication.DisconnectSerialAsync();
            var result = await _wifiCommunication.ConnectAsync(ipAddress, port);
            PublishCombinedStatus(result ? $"WiFi 已连接: {ipAddress}:{port}" : "WiFi 连接失败");
            return result;
        }

        /// <summary>
        /// 通过串口连接设备（自动断开 WiFi）
        /// </summary>
        /// <param name="portName">串口名称（如 COM3）</param>
        /// <param name="baudRate">波特率</param>
        /// <returns>连接成功返回 <c>true</c></returns>
        public async Task<bool> ConnectSerialAsync(string portName, int baudRate)
        {
            _currentMode = CommunicationMode.Serial;
            await _wifiCommunication.DisconnectAsync();
            var result = await _serialCommunication.ConnectSerialAsync(portName, baudRate);
            PublishCombinedStatus(result ? $"串口已连接: {portName} @ {baudRate}" : "串口连接失败");
            return result;
        }

        /// <summary>断开全部通信连接（WiFi 和串口）</summary>
        public async Task DisconnectAsync()
        {
            await _wifiCommunication.DisconnectAsync();
            await _serialCommunication.DisconnectSerialAsync();
            _currentMode = CommunicationMode.None;
            PublishCombinedStatus("已断开全部通信");
        }

        /// <summary>断开串口连接</summary>
        public async Task DisconnectSerialAsync()
        {
            await _serialCommunication.DisconnectSerialAsync();
            if (_currentMode == CommunicationMode.Serial)
            {
                _currentMode = CommunicationMode.None;
            }
            PublishCombinedStatus("串口已断开");
        }

        /// <summary>
        /// 向设备发送命令（自动选择当前活跃通道）
        /// <para>优先使用当前模式的通道；若当前模式无连接，回退到任一已连接通道。</para>
        /// </summary>
        /// <param name="command">命令字节（参见 <see cref="ProtocolCommandCode"/>）</param>
        /// <param name="data">命令附加数据</param>
        /// <param name="transport">传输方式（TCP/UDP）</param>
        public async Task SendCommandAsync(byte command, byte[]? data = null, TransportType transport = TransportType.TCP)
        {
            var activeService = GetActiveService();
            if (activeService == null)
            {
                _logger.LogWarning("当前没有可用的通信模式，命令未发送: 0x{Command:X2}", command);
                return;
            }

            await activeService.SendCommandAsync(command, data, transport);
        }

        /// <summary>
        /// 获取当前可用的通信服务
        /// <para>优先级：当前模式对应的服务 → 任一已连接的服务 → null</para>
        /// </summary>
        private ICommunicationService? GetActiveService()
        {
            return _currentMode switch
            {
                CommunicationMode.WiFi when _wifiCommunication.IsConnected => _wifiCommunication,
                CommunicationMode.Serial when _serialCommunication.IsConnected => _serialCommunication,
                _ when _wifiCommunication.IsConnected => _wifiCommunication,
                _ when _serialCommunication.IsConnected => _serialCommunication,
                _ => null
            };
        }

        private void OnInnerSystemStatusChanged(object? sender, SystemStatus status)
        {
            var combined = new SystemStatus
            {
                WifiState = _wifiCommunication.IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected,
                SerialState = _serialCommunication.IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected,
                Latency = status.Latency,
                TcpLatency = status.TcpLatency,
                UdpLatency = status.UdpLatency,
                SerialLatency = status.SerialLatency,
                IsSerialTransport = status.IsSerialTransport,
                PacketLoss = status.PacketLoss,
                IsDeviceConnected = status.IsDeviceConnected,
                UdpPacketsReceived = status.UdpPacketsReceived,
                UdpPacketsDropped = status.UdpPacketsDropped,
                LastLog = status.LastLog
            };

            SystemStatusChanged?.Invoke(this, combined);
        }

        private static bool IsActiveSender(object? sender, object active) => ReferenceEquals(sender, active);

        private void ForwardDataReceived(object? sender, byte[] data)
        {
            if (IsCurrentSender(sender))
            {
                DataReceived?.Invoke(this, data);
            }
        }

        private void ForwardFrameParsed(object? sender, ParsedFrame frame)
        {
            if (IsCurrentSender(sender))
            {
                FrameParsed?.Invoke(this, frame);
            }
        }

        private void ForwardPressureDataReceived(object? sender, PressureDataReport report)
        {
            if (IsCurrentSender(sender))
            {
                PressureDataReceived?.Invoke(this, report);
            }
        }

        private void ForwardEdemaDataReceived(object? sender, EdemaDataReport report)
        {
            if (IsCurrentSender(sender))
            {
                EdemaDataReceived?.Invoke(this, report);
            }
        }

        private void ForwardMotorStatusReceived(object? sender, MotorStatusReport report)
        {
            if (IsCurrentSender(sender))
            {
                MotorStatusReceived?.Invoke(this, report);
            }
        }

        private void ForwardTherapyProgressReceived(object? sender, TherapyProgressReport report)
        {
            if (IsCurrentSender(sender))
            {
                TherapyProgressReceived?.Invoke(this, report);
            }
        }

        private void ForwardMotorPositionReceived(object? sender, MotorPositionResponse position)
        {
            if (IsCurrentSender(sender))
            {
                MotorPositionReceived?.Invoke(this, position);
            }
        }

        private void ForwardResponseReceived(object? sender, GeneralResponse response)
        {
            if (IsCurrentSender(sender))
            {
                ResponseReceived?.Invoke(this, response);
            }
        }

        /// <summary>
        /// 判断事件发送者是否为当前活跃通道
        /// <para>防止非活跃通道的残留事件被错误转发给上层。</para>
        /// </summary>
        private bool IsCurrentSender(object? sender)
        {
            return _currentMode switch
            {
                CommunicationMode.WiFi => ReferenceEquals(sender, _wifiCommunication),
                CommunicationMode.Serial => ReferenceEquals(sender, _serialCommunication),
                _ => false
            };
        }

        private void PublishCombinedStatus(string lastLog)
        {
            SystemStatusChanged?.Invoke(this, new SystemStatus
            {
                WifiState = _wifiCommunication.IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected,
                SerialState = _serialCommunication.IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected,
                Latency = 0,
                TcpLatency = 0,
                UdpLatency = 0,
                SerialLatency = 0,
                IsSerialTransport = _currentMode == CommunicationMode.Serial,
                PacketLoss = 0,
                IsDeviceConnected = IsConnected,
                LastLog = lastLog
            });
        }
    }
}
