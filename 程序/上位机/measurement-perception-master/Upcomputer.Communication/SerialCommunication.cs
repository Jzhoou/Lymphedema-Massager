using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Upcomputer.Common.Enums;
using Upcomputer.Common.Logger;
using Upcomputer.Communication.Protocol;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;
using static Upcomputer.Communication.Protocol.ProtocolParser;

namespace Upcomputer.Communication
{
    /// <summary>
    /// 串口通信服务实现
    /// <para>
    /// 通过 <see cref="System.IO.Ports.SerialPort"/> 与下位机进行串口通信。
    /// 实现完整的协议帧收发、缓冲区管理、连接监控（热拔插检测），
    /// 并在串口连接断开时自动清理资源。
    /// </para>
    /// </summary>
    public sealed class SerialCommunication : ICommunicationService, IDisposable
    {
        private readonly ILogger<SerialCommunication> _logger;
        private readonly ProtocolParser _parser = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        // Serial receive buffer (align behavior with WifiCommunication)
        private readonly List<byte> _serialBuffer = new();
        private readonly object _serialBufferLock = new();
        private readonly object _heartbeatLock = new();
        private SerialPort? _serialPort;
        private CancellationTokenSource? _cancellationTokenSource;
        private CancellationTokenSource? _monitorCancellationTokenSource;
        private Task? _monitorTask;
        private Timer? _ntpTimer;
        private Timer? _heartbeatTimer;
        private ConnectionState _connectionState = ConnectionState.Disconnected;
        private double _latency;
        private int _packetsSent;
        private bool _deviceConnected;
        private string? _connectedPortName;

        /// <summary>最近一次因拔出而断开的串口名（用于过滤刷新列表中残留的幽灵端口）</summary>
        private string? _lastStalePortName;

        private long _pendingSerialHeartbeatSentAtMs = -1;
        private int _missedHeartbeatChecks;
        private int _awaitingHeartbeatResponse;
        private DateTime _lastHeartbeatTime;

        /// <summary>防止 CleanupSerialResources 被并发多次调用</summary>
        private int _cleanupGuard;

        private const byte HEARTBEAT_CMD = ProtocolCommandCode.Heartbeat;
        private const byte DIR_TO_DEVICE = ProtocolConstants.DirectionToDevice;

        /// <summary>连续心跳未响应次数阈值，达到后标记设备离线</summary>
        private const int HEARTBEAT_MISS_THRESHOLD = 5;

        /// <summary>心跳发送间隔（毫秒），10秒周期</summary>
        private const int HEARTBEAT_INTERVAL_MS = 10000;

        /// <summary>NTP时间同步发送间隔（毫秒），60秒周期</summary>
        private const int NTP_INTERVAL_MS = 60000;

        public event EventHandler<SystemStatus>? SystemStatusChanged;
        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<ParsedFrame>? FrameParsed;
        public event EventHandler<PressureDataReport>? PressureDataReceived;
        public event EventHandler<EdemaDataReport>? EdemaDataReceived;
        public event EventHandler<MotorStatusReport>? MotorStatusReceived;
        public event EventHandler<TherapyProgressReport>? TherapyProgressReceived;
        public event EventHandler<MotorPositionResponse>? MotorPositionReceived;
        public event EventHandler<GeneralResponse>? ResponseReceived;

        /// <summary>
        /// 底层监控检测到设备物理拔出时直接触发。
        /// <para>不经过 Router 的 SystemStatus 合并逻辑，UI 可借此直接弹出重连弹窗。</para>
        /// </summary>
        public event EventHandler<string>? DevicePhysicallyDisconnected;

        /// <summary>创建串口通信服务实例</summary>
        public SerialCommunication()
        {
            _logger = AppLogger.CreateLogger<SerialCommunication>();
            _parser.FrameReceived += OnParserFrameReceived;
        }

        /// <summary>
        /// 获取当前 UTC+8 时间戳（秒）
        /// </summary>
        private static long GetUtc8TimeSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 28800;
        }

        /// <summary>
        /// 判断是否为不需要时间戳的命令（ACK/NAK/心跳）
        /// </summary>
        private static bool IsNoTimestampCommand(byte command)
        {
            return command is ProtocolCommandCode.Ack or ProtocolCommandCode.Nak or ProtocolCommandCode.Heartbeat;
        }

        /// <summary>
        /// 从接收到的数据中提取时间戳
        /// 支持两种格式：
        ///   flag=0x01 + 8字节毫秒时间戳（下位机直连格式，1+8=9字节）
        ///   flag=0x01 + 6字节秒时间戳（WiFi中转兼容格式，1+6=7字节）
        /// </summary>
        private static bool TryExtractTimestampPayload(byte[] data, out long timestampSec, out byte[] payload, out bool isAbsoluteTime)
        {
            timestampSec = 0;
            payload = data;
            isAbsoluteTime = false;

            if (data.Length < 1 + 6)
            {
                return false;
            }

            byte flag = data[0];

            if (flag == 0x01)
            {
                // 优先尝试8字节毫秒时间戳（下位机直连格式）
                if (data.Length >= 1 + 8)
                {
                    timestampSec = (long)data[1]
                        | ((long)data[2] << 8)
                        | ((long)data[3] << 16)
                        | ((long)data[4] << 24)
                        | ((long)data[5] << 32)
                        | ((long)data[6] << 40)
                        | ((long)data[7] << 48)
                        | ((long)data[8] << 56);
                    payload = data.Length == 1 + 8 ? Array.Empty<byte>() : data[(1 + 8)..];
                    isAbsoluteTime = true;
                    return true;
                }

                // 降级为6字节秒时间戳（WiFi中转兼容格式）
                if (data.Length >= 1 + 6)
                {
                    timestampSec = (long)data[1]
                        | ((long)data[2] << 8)
                        | ((long)data[3] << 16)
                        | ((long)data[4] << 24)
                        | ((long)data[5] << 32)
                        | ((long)data[6] << 40);
                    payload = data.Length == 1 + 6 ? Array.Empty<byte>() : data[(1 + 6)..];
                    isAbsoluteTime = true;
                    return true;
                }
            }

            return false;
        }

        public bool IsConnected => _connectionState == ConnectionState.Connected;

        public string? ConnectedPortName => _connectedPortName;

        public bool IsConnectedPortPresent => !string.IsNullOrWhiteSpace(_connectedPortName) && Array.IndexOf(SerialPort.GetPortNames(), _connectedPortName) >= 0;

        /// <summary>
        /// 判断指定串口是否已知为"幽灵端口"（注册表仍有记录但底层句柄已失效，常见于 OTG/CDC 设备拔出后）
        /// </summary>
        public bool IsPortLikelyStale(string portName)
        {
            return !string.IsNullOrWhiteSpace(_lastStalePortName)
                && string.Equals(_lastStalePortName, portName, StringComparison.OrdinalIgnoreCase)
                && _connectionState != ConnectionState.Connected;
        }

        /// <summary>WiFi 连接（串口实现不支持，始终返回 false）</summary>
        public Task<bool> ConnectAsync(string ipAddress, int port)
        {
            return Task.FromResult(false);
        }

        /// <summary>
        /// 通过串口连接设备
        /// <para>配置串口参数（8N1，DTR/RTS 启用，读写超时 1000ms），打开端口后启动连接监控任务。</para>
        /// </summary>
        /// <param name="portName">串口名称（如 COM3）</param>
        /// <param name="baudRate">波特率（如 115200）</param>
        /// <returns>连接成功返回 <c>true</c></returns>
        public async Task<bool> ConnectSerialAsync(string portName, int baudRate)
        {
            try
            {
                await DisconnectSerialAsync();

                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    Encoding = System.Text.Encoding.UTF8,
                    NewLine = "\n",
                    DtrEnable = true,
                    RtsEnable = true,
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                _cancellationTokenSource = new CancellationTokenSource();
                _serialPort.DataReceived += OnSerialDataReceived;
                _serialPort.ErrorReceived += OnSerialErrorReceived;
                _serialPort.Open();
                _connectedPortName = portName;

                System.Threading.Interlocked.Exchange(ref _cleanupGuard, 0);
                _lastStalePortName = null;

                _monitorCancellationTokenSource = new CancellationTokenSource();
                _monitorTask = Task.Run(() => MonitorSerialConnectionAsync(_monitorCancellationTokenSource.Token));

                _connectionState = ConnectionState.Connected;
                _deviceConnected = true;
                System.Threading.Interlocked.Exchange(ref _missedHeartbeatChecks, 0);
                System.Threading.Interlocked.Exchange(ref _awaitingHeartbeatResponse, 0);
                UpdateStatus("串口已连接");

                // 每10秒发送一次心跳
                _heartbeatTimer = new Timer(async _ => await SendHeartbeatAsync(), null, 0, HEARTBEAT_INTERVAL_MS);

                // 每60秒发送一次NTP时间同步
                _ntpTimer = new Timer(async _ => await SendSetTimeAsync(), null, 0, NTP_INTERVAL_MS);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "串口连接失败: {Port}@{Baud}", portName, baudRate);
                _connectionState = ConnectionState.Error;
                UpdateStatus($"串口连接失败: {ex.Message}");
                await DisconnectSerialAsync();
                return false;
            }
        }

        public Task DisconnectAsync()
        {
            return DisconnectSerialAsync();
        }

        /// <summary>
        /// 内部断开：仅清理资源，不等待监控任务（避免监控内部调用时死锁）
        /// </summary>
        private void DisconnectSerialInternal()
        {
            _ntpTimer?.Dispose();
            _ntpTimer = null;

            try
            {
                CleanupSerialResources();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "串口断开异常");
            }
        }

        public async Task DisconnectSerialAsync()
        {
            var monitorTask = _monitorTask;

            // 先取消监控循环，防止竞争
            _monitorCancellationTokenSource?.Cancel();

            // 先清理资源并触发事件
            DisconnectSerialInternal();

            // 等待监控任务退出（带超时保护，防止意外死锁）
            if (monitorTask != null)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await monitorTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("等待监控任务退出超时（3秒）");
                }
            }
        }

        public async Task SendCommandAsync(byte command, byte[]? data = null, TransportType transport = TransportType.TCP)
        {
            if (_serialPort?.IsOpen != true)
            {
                _logger.LogWarning("串口未连接，无法发送命令");
                return;
            }

            var frame = ProtocolParser.PackFrameWithTimestamp(command, DIR_TO_DEVICE, data);
            var token = _cancellationTokenSource?.Token ?? CancellationToken.None;

            await _sendLock.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await _serialPort.BaseStream.WriteAsync(frame, 0, frame.Length, token);
                await _serialPort.BaseStream.FlushAsync(token);
            }
            finally
            {
                _sendLock.Release();
            }

            _packetsSent++;
            if (command == HEARTBEAT_CMD)
            {
                System.Threading.Interlocked.Exchange(ref _pendingSerialHeartbeatSentAtMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                System.Threading.Interlocked.Exchange(ref _awaitingHeartbeatResponse, 1);
            }

            UpdateConnectionStatus($"串口发送: 0x{command:X2}");
        }

        public void Dispose()
        {
            _parser.FrameReceived -= OnParserFrameReceived;
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            _ntpTimer?.Dispose();
            _ntpTimer = null;
            CleanupSerialResources();
            _monitorTask = null;
            _serialPort = null;
            _cancellationTokenSource = null;
            _monitorCancellationTokenSource = null;
            _sendLock.Dispose();
        }

        private void CleanupSerialResources()
        {
            // 防止并发多次清理
            if (System.Threading.Interlocked.CompareExchange(ref _cleanupGuard, 1, 0) != 0)
            {
                return;
            }

            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            _monitorCancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Cancel();

            try
            {
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= OnSerialDataReceived;
                    _serialPort.ErrorReceived -= OnSerialErrorReceived;

                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }

                    _serialPort.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "串口资源清理异常（设备可能已拔出）");
            }
            finally
            {
                _serialPort = null;
            }

            _lastStalePortName = _connectedPortName;
            _connectedPortName = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _monitorCancellationTokenSource?.Dispose();
            _monitorCancellationTokenSource = null;
            _connectionState = ConnectionState.Disconnected;
            _deviceConnected = false;
            UpdateConnectionStatus("串口已断开");
        }

        private void OnSerialDataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort?.IsOpen != true)
                {
                    return;
                }

                var bytesToRead = _serialPort.BytesToRead;
                if (bytesToRead <= 0)
                {
                    return;
                }

                var buffer = new byte[bytesToRead];
                var read = _serialPort.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    return;
                }

                var rawData = new byte[read];
                Array.Copy(buffer, rawData, read);

                // 与 WiFi 保持一致：先追加到底层缓冲区，再尝试抽取完整帧并处理。
                DataReceived?.Invoke(this, rawData);

                lock (_serialBufferLock)
                {
                    _serialBuffer.AddRange(rawData);
                }

                // 标记设备在线并重置心跳计数
                _deviceConnected = true;
                System.Threading.Interlocked.Exchange(ref _missedHeartbeatChecks, 0);

                while (TryExtractFrame()) { }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "串口读取失败，可能已拔出");
                _ = Task.Run(async () => await DisconnectSerialAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "串口接收失败");
            }
        }

        private void OnSerialErrorReceived(object? sender, SerialErrorReceivedEventArgs e)
        {
            _logger.LogWarning("串口错误: {Error}", e.EventType);
            UpdateStatus($"串口错误: {e.EventType}");
        }

        private void OnParserFrameReceived(object? sender, FrameReceivedEventArgs e)
        {
            var frame = e.Frame;

            // 串口延迟以心跳 RTT 为准。这里仅剥离时间戳前缀，避免把不同时间单位混算为异常超大延迟。
            if (frame.IsFromDevice && !IsNoTimestampCommand(frame.Command) && TryExtractTimestampPayload(frame.Data, out var timestampMs, out var payload, out var isAbsoluteTime))
            {
                frame.Data = payload;
            }

            FrameParsed?.Invoke(this, frame);
            OnParsedFrameReceived(frame);
        }

        private void OnParsedFrameReceived(ParsedFrame frame)
        {
            // 收到有效帧，重置心跳未响应计数
            _deviceConnected = true;
            System.Threading.Interlocked.Exchange(ref _missedHeartbeatChecks, 0);

            // 如果是心跳/ACK/NAK响应，计算 RTT 并清除等待标记
            if (frame.Command is ProtocolCommandCode.Heartbeat or ProtocolCommandCode.Ack or ProtocolCommandCode.Nak)
            {
                var sentAt = System.Threading.Interlocked.Exchange(ref _pendingSerialHeartbeatSentAtMs, -1);
                if (sentAt >= 0)
                {
                    _latency = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sentAt);
                }
                System.Threading.Interlocked.Exchange(ref _awaitingHeartbeatResponse, 0);
            }

            UpdateConnectionStatus();
            ParseAndRaiseDataEvent(frame);
        }

        private void ParseAndRaiseDataEvent(ParsedFrame frame)
        {
            try
            {
                switch (frame.Command)
                {
                    case ProtocolCommandCode.PressureData:
                        PressureDataReceived?.Invoke(this, DataParser.ParsePressureData(frame.Data));
                        break;
                    case ProtocolCommandCode.EdemaData:
                        EdemaDataReceived?.Invoke(this, DataParser.ParseEdemaData(frame.Data));
                        break;
                    case ProtocolCommandCode.MotorStatus:
                        MotorStatusReceived?.Invoke(this, DataParser.ParseMotorStatus(frame.Data));
                        break;
                    case ProtocolCommandCode.TherapyProgress:
                        TherapyProgressReceived?.Invoke(this, DataParser.ParseTherapyProgress(frame.Data));
                        break;
                    case ProtocolCommandCode.MotorPosition:
                        var motorPos = DataParser.ParseMotorPosition(frame.Data);
                        MotorPositionReceived?.Invoke(this, motorPos);
                        _logger.LogDebug($"收到电机位置: 2号={motorPos.Motor2EncoderValue}, 4号={motorPos.Motor4EncoderValue}");
                        break;
                    case ProtocolCommandCode.Ack:
                    case ProtocolCommandCode.Nak:
                        var response = DataParser.ParseGeneralResponse(frame.Data);
                        response.Result = (byte)(frame.Command == ProtocolCommandCode.Ack ? 0 : 1);
                        ResponseReceived?.Invoke(this, response);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析串口帧失败");
            }
        }

        // ------------------ 帮助方法（抽帧、日志） ------------------
        private static string HexDump(ReadOnlySpan<byte> data, int maxBytes = 64)
        {
            if (data.IsEmpty)
                return string.Empty;

            var length = Math.Min(data.Length, maxBytes);
            return BitConverter.ToString(data.Slice(0, length).ToArray()) + (data.Length > maxBytes ? " ..." : string.Empty);
        }

        private static string DescribeFrame(byte[] frame)
        {
            if (frame.Length < 9)
                return $"frame too short: {frame.Length} bytes, raw={HexDump(frame)}";

            var dataLength = (ushort)(frame[2] | (frame[3] << 8));
            var expectedTotalLength = 2 + 2 + dataLength + 2 + 1;
            var command = frame[4];
            var direction = frame[5];
            var crcIndex = Math.Min(frame.Length - 3, frame.Length - 1);
            var receivedCrc = frame.Length >= 3 ? (ushort)(frame[crcIndex] | (frame[crcIndex + 1] << 8)) : (ushort)0;

            return $"len={frame.Length}, expected={expectedTotalLength}, header={frame[0]:X2}-{frame[1]:X2}, dataLen={dataLength}, cmd=0x{command:X2}, dir=0x{direction:X2}, crc=0x{receivedCrc:X4}, tail=0x{frame[^1]:X2}, raw={HexDump(frame)}";
        }

        private bool TryExtractFrame()
        {
            byte[]? extractedFrame = null;

            lock (_serialBufferLock)
            {
                if (_serialBuffer.Count < 9) // 最小帧长
                    return false;

                var hex = HexDump(_serialBuffer.ToArray(), 32);
                _logger.LogTrace($"TryExtractFrame 缓冲区状态 count={_serialBuffer.Count}, data={hex}");

                // 查找帧头 AA 55
                int headerIndex = -1;
                for (int i = 0; i < _serialBuffer.Count - 1; i++)
                {
                    if (_serialBuffer[i] == 0xAA && _serialBuffer[i + 1] == 0x55)
                    {
                        headerIndex = i;
                        break;
                    }
                }

                if (headerIndex < 0)
                {
                    _logger.LogWarning($"未找到串口帧头，当前缓冲区={HexDump(_serialBuffer.ToArray(), 64)}");
                    CleanInvalidData();
                    return false;
                }

                if (headerIndex > 0)
                {
                    _serialBuffer.RemoveRange(0, headerIndex);
                }

                if (_serialBuffer.Count < 4)
                {
                    _logger.LogTrace("串口帧头已找到，但长度字段未收齐");
                    return false;
                }

                ushort dataLength = (ushort)(_serialBuffer[2] | (_serialBuffer[3] << 8));

                if (dataLength < 2 || dataLength > ProtocolConstants.MaxDataLength)
                {
                    _logger.LogWarning($"串口无效的数据长度: {dataLength}");
                    _serialBuffer.RemoveAt(0);
                    return true;
                }

                int totalFrameLength = 2 + 2 + dataLength + 2 + 1;

                if (_serialBuffer.Count < totalFrameLength)
                {
                    _logger.LogTrace($"串口帧不完整: 需要{totalFrameLength}, 当前{_serialBuffer.Count}");
                    return false;
                }

                if (_serialBuffer[totalFrameLength - 1] != ProtocolConstants.FrameTail)
                {
                    _logger.LogWarning($"串口帧尾错误: 期望0x{ProtocolConstants.FrameTail:X2}, 实际0x{_serialBuffer[totalFrameLength - 1]:X2}");
                    _serialBuffer.RemoveAt(0);
                    return true;
                }

                extractedFrame = _serialBuffer.GetRange(0, totalFrameLength).ToArray();
                _serialBuffer.RemoveRange(0, totalFrameLength);

                _logger.LogDebug($"串口提取到完整帧: {DescribeFrame(extractedFrame)}");
            }

            if (extractedFrame != null)
            {
                ProcessExtractedFrame(extractedFrame);
            }

            return true;
        }

        private void CleanInvalidData()
        {
            int lastAA = -1;
            for (int i = _serialBuffer.Count - 1; i >= 0; i--)
            {
                if (_serialBuffer[i] == 0xAA)
                {
                    lastAA = i;
                    break;
                }
            }

            if (lastAA > 0)
            {
                int removeCount = lastAA;
                _logger.LogDebug($"串口清理 {removeCount} 字节无效数据");
                _serialBuffer.RemoveRange(0, removeCount);
            }
            else if (lastAA < 0)
            {
                _logger.LogDebug($"串口无有效数据，清空 {_serialBuffer.Count} 字节");
                _serialBuffer.Clear();
            }
        }

        private void ProcessExtractedFrame(byte[] frame)
        {
            try
            {
                _logger.LogDebug($"串口处理接收帧: {DescribeFrame(frame)}");
                DataReceived?.Invoke(this, frame);
                _parser.FeedData(frame);
                _logger.LogTrace($"已喂入协议解析器（串口）: 命令=0x{frame[4]:X2}, 长度={frame.Length}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理串口接收帧异常: {DescribeFrame(frame)}");
            }
        }

        private void UpdateStatus(string message)
        {
            UpdateConnectionStatus(message);
        }

        private void UpdateConnectionStatus(string? lastLog = null)
        {
            SystemStatusChanged?.Invoke(this, new SystemStatus
            {
                WifiState = ConnectionState.Disconnected,
                SerialState = _connectionState,
                IsDeviceConnected = _deviceConnected,
                TcpLatency = _latency,
                Latency = _latency,
                UdpLatency = 0,
                SerialLatency = _latency,
                IsSerialTransport = true,
                PacketLoss = 0,
                LastLog = lastLog ?? (_connectionState == ConnectionState.Connected ? "串口已连接" : "串口未连接")
            });
        }

        /// <summary>
        /// 发送NTP时间同步命令（SetTime 0x36），6字节小端序UTC+8秒
        /// </summary>
        private async Task SendSetTimeAsync()
        {
            if (!IsConnected) return;

            try
            {
                var timeData = Protocol.CommandBuilder.BuildSetTime();
                await SendCommandAsync(ProtocolCommandCode.SetTime, timeData);
                _logger.LogDebug($"NTP时间同步已发送: {GetUtc8TimeSeconds()}s");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NTP时间同步发送异常");
            }
        }

        private int _isHeartbeating = 0;

        private async Task SendHeartbeatAsync()
        {
            if (!IsConnected) return;

            // 防止并行发送导致心跳长期积压
            if (System.Threading.Interlocked.CompareExchange(ref _isHeartbeating, 1, 0) != 0) return;

            try
            {
                // 如果上一轮心跳还没收到设备回应，则累加未响应次数
                if (System.Threading.Interlocked.CompareExchange(ref _awaitingHeartbeatResponse, 0, 0) == 1)
                {
                    var missed = System.Threading.Interlocked.Increment(ref _missedHeartbeatChecks);
                    if (missed >= HEARTBEAT_MISS_THRESHOLD)
                    {
                        _logger.LogWarning($"[警告] 连续 {missed} 次心跳未收到设备回应，设备离线");
                        _deviceConnected = false;
                        UpdateConnectionStatus($"设备多次未响应心跳，已标记离线");
                    }
                }
                else
                {
                    System.Threading.Interlocked.Exchange(ref _missedHeartbeatChecks, 0);
                }

                _lastHeartbeatTime = DateTime.Now;
                await SendCommandAsync(HEARTBEAT_CMD);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "串口心跳发送异常");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _isHeartbeating, 0);
            }
        }

        private async Task MonitorSerialConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, cancellationToken);

                    var port = _serialPort;
                    if (port == null || string.IsNullOrWhiteSpace(_connectedPortName))
                    {
                        continue;
                    }

                    // 检查1：端口是否仍在系统 SerialPort 列表中（检测注册表更新的拔出）
                    var portExists = Array.IndexOf(SerialPort.GetPortNames(), _connectedPortName) >= 0;

                    // 检查2：底层句柄是否仍然有效（主动探测，解决 OTG 拔出后注册表延迟问题）
                    bool portAccessible = false;
                    try
                    {
                        if (port.IsOpen)
                        {
                            _ = port.BytesToRead;
                            portAccessible = true;
                        }
                    }
                    catch (Exception)
                    {
                        portAccessible = false;
                    }

                    if (!portExists || !portAccessible)
                    {
                        var reason = !portExists
                            ? "端口已从系统移除"
                            : "底层串口句柄已失效（设备物理拔出）";
                        _logger.LogWarning(
                            "串口设备已拔出: {Port}, 端口存在={PortExists}, 可访问={PortAccessible}",
                            _connectedPortName, portExists, portAccessible);
                        // 使用内部断开方法，避免死锁（不能在这里 await DisconnectSerialAsync，因为它会等待本监控任务自身）
                        DisconnectSerialInternal();
                        // 触发物理断开事件，UI 可借此直接弹出重连弹窗（不依赖 SystemStatusChanged 状态转换）
                        DevicePhysicallyDisconnected?.Invoke(this, reason);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "串口监测循环异常退出");
            }
        }
    }
}