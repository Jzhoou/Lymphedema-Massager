using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.IO.Ports;
using System.Threading;
using Upcomputer.Common.Enums;
using Upcomputer.Common.Logger;
using Upcomputer.Communication.Protocol;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;
using static Upcomputer.Communication.Protocol.ProtocolParser;

namespace Upcomputer.Communication
{
    /// <summary>
    /// WiFi 通信服务实现
    /// <para>
    /// 通过 TCP 连接设备并使用 UDP 接收高频传感器数据。
    /// 实现心跳保活机制（10秒周期）、TCP/UDP 双通道数据收发、协议帧解析、
    /// 设备在线检测（连续 5 次心跳未响应则标记离线）等功能。
    /// 延迟计算：TCP 通道通过心跳 RTT 测量，UDP 通道通过设备端时间戳差值计算。
    /// </para>
    /// </summary>
    public class WifiCommunication : ICommunicationService
    {
        private readonly ILogger<WifiCommunication> _logger;
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private UdpClient? _udpClient;
        private SerialPort? _serialPort;
        private CancellationTokenSource? _cancellationTokenSource;
        private CancellationTokenSource? _serialCancellationTokenSource;
        private Timer? _heartbeatTimer;
        private Timer? _ntpTimer;
        private Timer? _networkMonitorTimer;
        /// <summary>TCP 连接所使用的本地 IP，用于匹配对应的网络接口</summary>
        private IPAddress? _localBindAddress;
        private ConnectionState _connectionState;
        private ConnectionState _serialConnectionState;
        private double _latency;
        private double _packetLoss;
        private int _packetsSent;
        private int _packetsLost;
        private int _udpPacketsReceived;
        private int _udpPacketsDropped;
        private DateTime _lastHeartbeatTime;
        private DateTime _lastReceiveTime;
        private long _pendingTcpHeartbeatSentAtMs = -1;
        private double _udpLatency;
        private long _espTimeOffset = -1;

        // ⚠️ 添加锁保护 _tcpBuffer
        private readonly List<byte> _tcpBuffer = new();
        private readonly object _bufferLock = new();
        private readonly object _serialBufferLock = new();
        private readonly object _heartbeatLock = new();
        private readonly object _timeLock = new();  // 添加时间锁
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1); // 添加发送锁，确保 TCP 写操作不交错
        private int _missedHeartbeatChecks;
        private int _awaitingHeartbeatResponse;
        private int _deviceConnected;
        private int _isDisconnecting;
        private readonly AsyncLocal<bool> _trackLatencyForCurrentFeed = new();

        /// <summary>连续心跳未响应次数阈值，达到后标记设备离线</summary>
        private const int HEARTBEAT_MISS_THRESHOLD = 5;

        /// <summary>心跳发送间隔（毫秒），10秒周期避免占用过多带宽</summary>
        private const int HEARTBEAT_INTERVAL_MS = 10000;

        /// <summary>NTP时间同步发送间隔（毫秒），60秒周期</summary>
        private const int NTP_INTERVAL_MS = 60000;

        /// <summary>网络接口状态轮询间隔（毫秒），3秒一次检查 WiFi 网卡是否仍在工作</summary>
        private const int NETWORK_MONITOR_INTERVAL_MS = 3000;

        /// <summary>UDP 接收缓冲区大小（1MB），防止高频数据丢包</summary>
        private const int UDP_RECEIVE_BUFFER_SIZE = 1024 * 1024;

        /// <summary>UDP 数据接收端口号（与下位机约定的固定端口）</summary>
        private const int UDP_DATA_PORT = 9528;


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
        /// 底层检测到 WiFi 连接物理断开或设备心跳超时时直接触发。
        /// <para>不经过 Router 的 SystemStatus 合并逻辑，UI 可借此直接弹出重连弹窗。</para>
        /// </summary>
        public event EventHandler<string>? DevicePhysicallyDisconnected;

        private readonly ProtocolParser _parser = new();

        private const byte HEARTBEAT_CMD = ProtocolCommandCode.Heartbeat;
        private const byte DIR_TO_DEVICE = ProtocolConstants.DirectionToDevice;

        /// <summary>创建 WiFi 通信服务实例</summary>
        public WifiCommunication()
        {
            _logger = AppLogger.CreateLogger<WifiCommunication>();
            _connectionState = ConnectionState.Disconnected;
            _serialConnectionState = ConnectionState.Disconnected;
            _deviceConnected = 0;
            _parser.FrameReceived += OnParserFrameReceived;
        }

        /// <summary>
        /// Windows 系统网络可用性/地址变更回调。
        /// </summary>
        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (!e.IsAvailable && IsConnected)
            {
                _logger.LogWarning("Windows 系统网络已断开，触发 WiFi 断联");
                DevicePhysicallyDisconnected?.Invoke(this, "系统网络已断开（Windows WiFi 断开）");
                _ = Task.Run(async () => await DisconnectAsync());
            }
        }

        /// <summary>
        /// 网络地址变更回调（WiFi 断开/重连时会触发），立即检查网卡状态。
        /// </summary>
        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            if (IsConnected)
            {
                CheckNetworkInterfaceAlive();
            }
        }

        private static readonly TimeSpan Utc8Offset = TimeSpan.FromHours(8);

        /// <summary>获取 UTC+8 Unix 秒时间戳</summary>
        private static long GetUtc8TimeSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 28800;
        }

        /// <summary>获取 UTC+8 Unix 毫秒时间戳</summary>
        private static long GetUtc8TimeMillis()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 28800L * 1000L;
        }

        private const long FIXED_OFFSET_COMPENSATION = 0; // 毫秒


        private void OnParserFrameReceived(object? sender, FrameReceivedEventArgs e)
        {
            var frame = e.Frame;
            // 对所有来自设备的数据帧（非 ACK/NAK/Heartbeat）尝试提取时间戳
            if (frame.IsFromDevice && !IsNoTimestampCommand(frame.Command) && TryExtractTimestampPayload(frame.Data, out var timestampMillis, out var payload, out var isAbsoluteTime))
            {
                if (isAbsoluteTime)
                {
                    // 8字节时间戳为毫秒级，使用毫秒比较
                    long nowMs = GetUtc8TimeMillis();
                    _udpLatency = Math.Max(0, nowMs - timestampMillis);
                    System.Diagnostics.Debug.WriteLine($"[WiFi] 电机位置帧时间戳延迟: {_udpLatency:F0}ms (对端: {timestampMillis}, 本地: {nowMs})");
                    _logger.LogDebug($"时间戳延迟: {_udpLatency:F0}ms (对端: {timestampMillis}, 本地: {nowMs})");
                }
                else
                {
                    // 6字节时间戳为秒级，使用秒比较
                    long nowSec = GetUtc8TimeSeconds();
                    if (_espTimeOffset == -1)
                    {
                        _espTimeOffset = nowSec - timestampMillis;
                    }

                    long espAbsoluteTime = _espTimeOffset + timestampMillis;
                    _udpLatency = Math.Max(0, nowSec - espAbsoluteTime);
                    _logger.LogDebug($"相对延迟: {_udpLatency:F1}s (估算)");
                }

                frame.Data = payload;
            }

            FrameParsed?.Invoke(this, frame);
            OnParsedFrameReceived(frame);
        }

        public bool IsConnected => _connectionState == ConnectionState.Connected || _serialConnectionState == ConnectionState.Connected;

        private bool IsDeviceConnected => System.Threading.Interlocked.CompareExchange(ref _deviceConnected, 0, 0) == 1;

        private static string HexDump(ReadOnlySpan<byte> data, int maxBytes = 64)
        {
            if (data.IsEmpty)
            {
                return string.Empty;
            }

            var length = Math.Min(data.Length, maxBytes);
            return BitConverter.ToString(data.Slice(0, length).ToArray()) + (data.Length > maxBytes ? " ..." : string.Empty);
        }

        private static string DescribeFrame(byte[] frame)
        {
            if (frame.Length < 9)
            {
                return $"frame too short: {frame.Length} bytes, raw={HexDump(frame)}";
            }

            var dataLength = (ushort)(frame[2] | (frame[3] << 8));
            var expectedTotalLength = 2 + 2 + dataLength + 2 + 1;
            var command = frame[4];
            var direction = frame[5];
            var crcIndex = Math.Min(frame.Length - 3, frame.Length - 1);
            var receivedCrc = frame.Length >= 3 ? (ushort)(frame[crcIndex] | (frame[crcIndex + 1] << 8)) : (ushort)0;

            return $"len={frame.Length}, expected={expectedTotalLength}, header={frame[0]:X2}-{frame[1]:X2}, dataLen={dataLength}, cmd=0x{command:X2}, dir=0x{direction:X2}, crc=0x{receivedCrc:X4}, tail=0x{frame[^1]:X2}, raw={HexDump(frame)}";
        }

        private void SetDeviceConnectionState(bool connected, string? logMessage = null)
        {
            var previous = System.Threading.Interlocked.Exchange(ref _deviceConnected, connected ? 1 : 0);
            if (previous == (connected ? 1 : 0) && string.IsNullOrWhiteSpace(logMessage))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(logMessage))
            {
                _logger.LogWarning(logMessage);
            }

            UpdateConnectionStatus();
        }

        private void UpdateLatencyForIncomingFrame(ParsedFrame frame)
        {
            if (!frame.IsFromDevice || frame.Command is not (HEARTBEAT_CMD or ProtocolCommandCode.Ack or ProtocolCommandCode.Nak))
            {
                return;
            }

            var sentAt = System.Threading.Interlocked.Exchange(ref _pendingTcpHeartbeatSentAtMs, -1);
            if (sentAt < 0)
            {
                return;
            }

            _latency = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sentAt);
            System.Threading.Interlocked.Exchange(ref _awaitingHeartbeatResponse, 0);
            _logger.LogDebug($"TCP 心跳 RTT: {_latency:F1}ms, Command=0x{frame.Command:X2}");
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

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            try
            {
                UpdateConnectionState(ConnectionState.Connecting);
                _logger.LogInformation($"WiFi 正在连接: {ipAddress}:{port}");

                _tcpClient = new TcpClient();

                var connectTask = _tcpClient.ConnectAsync(ipAddress, port);
                var timeoutTask = Task.Delay(5000);

                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    throw new TimeoutException("连接超时");
                }

                await connectTask;

                // 启用 TCP Keep-Alive（系统层面探测物理断电）
                _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _tcpClient.ReceiveTimeout = 0; // 不启用 Socket 自身的超时机制，依靠代码逻辑

                _networkStream = _tcpClient.GetStream();
                _tcpClient.NoDelay = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // 记录 TCP 连接使用的本地 IP，用于后续检测对应网卡是否存活
                _localBindAddress = (_tcpClient.Client.LocalEndPoint as IPEndPoint)?.Address;

                // 线程安全地更新时间
                lock (_timeLock)
                {
                    _lastReceiveTime = DateTime.Now;
                }
                System.Threading.Interlocked.Exchange(ref _missedHeartbeatChecks, 0);
                System.Threading.Interlocked.Exchange(ref _awaitingHeartbeatResponse, 0);
                System.Threading.Interlocked.Exchange(ref _deviceConnected, 1);

                // 清空缓冲区
                lock (_bufferLock)
                {
                    _tcpBuffer.Clear();
                }
                _parser.ClearBuffer();

                UpdateConnectionState(ConnectionState.Connected);
                _logger.LogInformation("WiFi 连接成功");
                _espTimeOffset = -1;

                _logger.LogInformation("启动 WiFi 接收循环");
                _logger.LogWarning("WiFi 接收循环即将启动（如果这里之后没有任何接收日志，说明读取循环未运行或被提前异常退出）");
                var receiveTask = Task.Run(() => ReceiveDataLoop(_cancellationTokenSource.Token));
                _ = receiveTask.ContinueWith(
                    t => _logger.LogError(t.Exception, "WiFi 接收循环异常退出"),
                    TaskContinuationOptions.OnlyOnFaulted);
                _ = receiveTask.ContinueWith(
                    t => _logger.LogWarning(t.IsCanceled ? "WiFi 接收循环已取消退出" : "WiFi 接收循环正常结束"),
                    TaskContinuationOptions.NotOnRanToCompletion);

                // 每10秒发送一次心跳，避免高频心跳占用通信带宽
                _heartbeatTimer = new Timer(async _ => await SendHeartbeatAsync(), null, 0, HEARTBEAT_INTERVAL_MS);

                // 每60秒发送一次NTP时间同步
                _ntpTimer = new Timer(async _ => await SendSetTimeAsync(), null, 0, NTP_INTERVAL_MS);

                // 每3秒快速检测网卡状态（WiFi 断开时网卡 OperationalStatus 立即变化）
                _networkMonitorTimer = new Timer(_ => CheckNetworkInterfaceAlive(), null, NETWORK_MONITOR_INTERVAL_MS, NETWORK_MONITOR_INTERVAL_MS);

                // 监听 Windows 系统网络变更，WiFi 断开时立即检测
                NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
                NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

                TryStartUdpReceiver();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WiFi 连接失败");
                DisposeUdpClient();
                UpdateConnectionState(ConnectionState.Error);
                return false;
            }
        }

        public async Task<bool> ConnectSerialAsync(string portName, int baudRate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(portName))
                {
                    throw new ArgumentException("串口名称不能为空", nameof(portName));
                }

                await DisconnectSerialAsync();

                _serialConnectionState = ConnectionState.Connecting;
                UpdateConnectionStatus();

                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    Encoding = System.Text.Encoding.UTF8,
                    NewLine = "\n",
                    ReadTimeout = 1000,
                    WriteTimeout = 1000,
                    DtrEnable = true,
                    RtsEnable = true
                };

                _serialCancellationTokenSource = new CancellationTokenSource();
                _serialPort.DataReceived += OnSerialDataReceived;
                _serialPort.ErrorReceived += OnSerialErrorReceived;
                _serialPort.Open();

                _serialConnectionState = ConnectionState.Connected;
                _logger.LogInformation($"串口连接成功: {portName} @ {baudRate}");
                UpdateConnectionStatus();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"串口连接失败: {portName} @ {baudRate}");
                await DisconnectSerialAsync();
                _serialConnectionState = ConnectionState.Error;
                UpdateConnectionStatus();
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            _logger.LogInformation("WiFi 断开连接...");

            if (System.Threading.Interlocked.Exchange(ref _isDisconnecting, 1) != 0)
            {
                return;
            }

            // 提早更新状态，阻止其它心跳再被投入或者被认为是已连接的
            if (_connectionState != ConnectionState.Disconnected)
            {
                 UpdateConnectionState(ConnectionState.Disconnected);
            }

            // 取消 Windows 网络变更监听
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

            _cancellationTokenSource?.Cancel();
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            _ntpTimer?.Dispose();
            _ntpTimer = null;
            _networkMonitorTimer?.Dispose();
            _networkMonitorTimer = null;
            _localBindAddress = null;
            _networkStream?.Close();
            _tcpClient?.Close();
            DisposeUdpClient();

            lock (_bufferLock)
            {
                _tcpBuffer.Clear();
            }
            _parser.ClearBuffer();
            _espTimeOffset = -1;
            System.Threading.Interlocked.Exchange(ref _missedHeartbeatChecks, 0);
            System.Threading.Interlocked.Exchange(ref _awaitingHeartbeatResponse, 0);
            System.Threading.Interlocked.Exchange(ref _deviceConnected, 0);
            System.Threading.Interlocked.Exchange(ref _udpPacketsReceived, 0);
            System.Threading.Interlocked.Exchange(ref _udpPacketsDropped, 0);
            System.Threading.Interlocked.Exchange(ref _isDisconnecting, 0);

            // 如果有必要再次触发事件（通常已通过前面调用触发）
            // UpdateConnectionState(ConnectionState.Disconnected); 
            await Task.CompletedTask;
        }

        public async Task DisconnectSerialAsync()
        {
            try
            {
                _serialCancellationTokenSource?.Cancel();
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= OnSerialDataReceived;
                    _serialPort.ErrorReceived -= OnSerialErrorReceived;
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "串口断开时发生异常");
            }
            finally
            {
                _serialPort?.Dispose();
                _serialPort = null;
                _serialCancellationTokenSource?.Dispose();
                _serialCancellationTokenSource = null;
                _serialConnectionState = ConnectionState.Disconnected;
                UpdateConnectionStatus();
            }

            await Task.CompletedTask;
        }

        public async Task SendCommandAsync(byte command, byte[]? data = null, Upcomputer.Common.Enums.TransportType transport = Upcomputer.Common.Enums.TransportType.TCP)
        {
            try
            {
                if (_serialConnectionState == ConnectionState.Connected && transport == Upcomputer.Common.Enums.TransportType.TCP && _connectionState != ConnectionState.Connected)
                {
                    await SendViaSerialAsync(command, data);
                    return;
                }

                if (transport == Upcomputer.Common.Enums.TransportType.UDP || ShouldUseUdp(command))
                {
                    await SendViaUdpAsync(command, data);
                    return;
                }

                await SendViaTcpAsync(command, data);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("发送命令被取消");
                Console.WriteLine("[SendCommandAsync] 发送命令被取消");
            }
            catch (Exception ex)
            {
                _packetsLost++;
                _logger.LogError(ex, "发送命令失败");
                Console.WriteLine(ex);
                UpdatePacketLoss();
            }
        }

        private async Task SendViaTcpAsync(byte command, byte[]? data)
        {
            if (!IsConnected || _networkStream == null || _cancellationTokenSource == null || _cancellationTokenSource.IsCancellationRequested)
            {
                _logger.LogWarning("TCP发送失败：未连接");
                return;
            }

            var frame = ProtocolParser.PackFrameWithTimestamp(command, DIR_TO_DEVICE, data);
            var token = _cancellationTokenSource.Token;

            await _sendLock.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await _networkStream.WriteAsync(frame, token);
                await _networkStream.FlushAsync(token);
            }
            finally
            {
                _sendLock.Release();
            }

            _packetsSent++;
            if (command == HEARTBEAT_CMD)
            {
                System.Threading.Interlocked.Exchange(ref _pendingTcpHeartbeatSentAtMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                System.Threading.Interlocked.Exchange(ref _awaitingHeartbeatResponse, 1);
            }
            _logger.LogDebug($"TCP发送: 命令=0x{command:X2}, 长度={frame.Length}");
        }

        private async Task SendViaUdpAsync(byte command, byte[]? data)
        {
            if (_udpClient == null)
            {
                _logger.LogWarning("UDP发送失败：UDP客户端未初始化");
                await SendViaTcpAsync(command, data);
                return;
            }

            if (_tcpClient?.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint)
            {
                _logger.LogWarning("UDP发送失败：无TCP连接，无法获取目标地址");
                return;
            }

            var frame = ProtocolParser.PackFrameWithTimestamp(command, DIR_TO_DEVICE, data);
            var udpEndPoint = new IPEndPoint(remoteEndPoint.Address, UDP_DATA_PORT);

            await _udpClient.SendAsync(frame, frame.Length, udpEndPoint);
            _packetsSent++;
            _logger.LogDebug($"UDP发送: 命令=0x{command:X2}, 长度={frame.Length}, 目标={udpEndPoint}");
        }

        private async Task SendViaSerialAsync(byte command, byte[]? data)
        {
            if (_serialPort?.IsOpen != true)
            {
                _logger.LogWarning("串口发送失败：串口未打开");
                return;
            }

            var frame = ProtocolParser.PackFrameWithTimestamp(command, DIR_TO_DEVICE, data);
            await _serialPort.BaseStream.WriteAsync(frame, 0, frame.Length, _serialCancellationTokenSource?.Token ?? CancellationToken.None);
            await _serialPort.BaseStream.FlushAsync(_serialCancellationTokenSource?.Token ?? CancellationToken.None);
            _packetsSent++;
            _logger.LogDebug($"串口发送: 命令=0x{command:X2}, 长度={frame.Length}");
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
                lock (_serialBufferLock)
                {
                    _tcpBuffer.AddRange(rawData);
                }

                System.Threading.Interlocked.Exchange(ref _deviceConnected, 1);
                _serialConnectionState = ConnectionState.Connected;
                UpdateConnectionStatus();

                while (TryExtractFrame())
                {
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "串口接收失败");
            }
        }

        private void OnSerialErrorReceived(object? sender, SerialErrorReceivedEventArgs e)
        {
            _logger.LogWarning($"串口错误: {e.EventType}");
            _serialConnectionState = ConnectionState.Error;
            UpdateConnectionStatus();
        }

        private void TryStartUdpReceiver()
        {
            if (_cancellationTokenSource == null || _udpClient != null)
            {
                return;
            }

            try
            {
                _udpClient = new UdpClient(UDP_DATA_PORT);
                _udpClient.Client.ReceiveTimeout = 0;
                _udpClient.Client.ReceiveBufferSize = UDP_RECEIVE_BUFFER_SIZE;

                _logger.LogInformation($"UDP数据接收已启动，端口: {UDP_DATA_PORT}");
                var udpReceiveTask = Task.Run(() => ReceiveUdpLoop(_cancellationTokenSource.Token));
                _ = udpReceiveTask.ContinueWith(
                    t => _logger.LogError(t.Exception, "UDP 接收循环异常退出"),
                    TaskContinuationOptions.OnlyOnFaulted);
                _ = udpReceiveTask.ContinueWith(
                    t => _logger.LogWarning(t.IsCanceled ? "UDP 接收循环已取消退出" : "UDP 接收循环正常结束"),
                    TaskContinuationOptions.NotOnRanToCompletion);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"UDP 接收监听启动失败，端口: {UDP_DATA_PORT}");
                DisposeUdpClient();
            }
        }

        private void DisposeUdpClient()
        {
            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;
        }

        private async Task ReceiveDataLoop(CancellationToken cancellationToken)
        {
            try
            {
                var buffer = new byte[4096];

                _logger.LogInformation("ReceiveDataLoop 已启动");
                Console.WriteLine("[ReceiveDataLoop] 已启动");
                _logger.LogWarning("ReceiveDataLoop 已进入等待读取状态");
                Console.WriteLine("Received data");

                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    try
                    {
                        if (_networkStream == null)
                        {
                            break;
                        }

                        _logger.LogTrace("ReceiveDataLoop 等待 TCP 数据");

                        // 直接等待数据到达；不要依赖 DataAvailable 轮询，避免错过短包和造成假空闲
                        int bytesRead = await _networkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);

                        if (bytesRead > 0)
                        {
                            _logger.LogWarning($"ReceiveDataLoop 读取到 {bytesRead} 字节");
                            var rxHex = HexDump(buffer.AsSpan(0, bytesRead));
                            _logger.LogWarning($"TCP RX 原始数据: {rxHex}");
                            System.Diagnostics.Debug.WriteLine($"[TCP RX] {bytesRead} bytes: {rxHex}");

                            // 只要从建立的流中接收到任何底层数据，就更新最后接收时间
                            // 避免因为上层解包错误或粘包导致判断为设备离线
                            lock (_timeLock)
                            {
                                _lastReceiveTime = DateTime.Now;
                            }
                            System.Threading.Interlocked.Exchange(ref _missedHeartbeatChecks, 0);
                            System.Threading.Interlocked.Exchange(ref _deviceConnected, 1);

                            byte[] rawData = new byte[bytesRead];
                            Array.Copy(buffer, rawData, bytesRead);

                            // 加锁添加数据
                            lock (_bufferLock)
                            {
                                _tcpBuffer.AddRange(rawData);
                                _logger.LogDebug($"TCP 缓冲区追加后: count={_tcpBuffer.Count}, head={HexDump(_tcpBuffer.ToArray())}");
                            }

                            // 持续尝试从缓冲区提取完整的帧
                            while (TryExtractFrame())
                            {
                                // 继续提取
                            }
                        }
                        else
                        {
                            _logger.LogWarning("WiFi 数据流返回 0 字节，远端已主动断开连接。");
                            DevicePhysicallyDisconnected?.Invoke(this, "WiFi 连接已断开（远端关闭）");
                            _ = Task.Run(async () => await DisconnectAsync());
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("ReceiveDataLoop 被取消");
                        Console.WriteLine("[ReceiveDataLoop] 接收循环被取消");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "接收数据异常，停止接收循环");
                        Console.WriteLine(ex);
                        DevicePhysicallyDisconnected?.Invoke(this, $"WiFi 连接异常断开: {ex.Message}");
                        _ = Task.Run(async () => await DisconnectAsync());
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReceiveDataLoop 启动失败");
            }
        }

        private async Task ReceiveUdpLoop(CancellationToken cancellationToken)
        {
            try
            {
                if (_udpClient == null)
                {
                    return;
                }

                _logger.LogInformation($"UDP数据接收循环已启动，端口: {UDP_DATA_PORT}");

                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    try
                    {
                        var result = await _udpClient.ReceiveAsync(cancellationToken);
                        var data = result.Buffer;

                        System.Threading.Interlocked.Increment(ref _udpPacketsReceived);
                        _logger.LogTrace($"UDP收到 {data.Length} 字节: {HexDump(data)}");

                        lock (_timeLock)
                        {
                            _lastReceiveTime = DateTime.Now;
                        }

                        System.Threading.Interlocked.Exchange(ref _missedHeartbeatChecks, 0);
                        System.Threading.Interlocked.Exchange(ref _deviceConnected, 1);

                        DataReceived?.Invoke(this, data);

                        var previousTrackLatency = _trackLatencyForCurrentFeed.Value;
                        _trackLatencyForCurrentFeed.Value = false;
                        try
                        {
                            _parser.FeedData(data);
                        }
                        finally
                        {
                            _trackLatencyForCurrentFeed.Value = previousTrackLatency;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("UDP接收循环被取消");
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogInformation("UDP接收客户端已释放，结束接收循环");
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Threading.Interlocked.Increment(ref _udpPacketsDropped);
                        _logger.LogError(ex, "UDP接收异常");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP接收循环启动失败");
            }
            finally
            {
                _logger.LogInformation($"UDP接收循环结束，共收到 {System.Threading.Interlocked.CompareExchange(ref _udpPacketsReceived, 0, 0)} 包，丢包 {System.Threading.Interlocked.CompareExchange(ref _udpPacketsDropped, 0, 0)}");
            }
        }

        private bool TryExtractFrame()
        {
            byte[]? extractedFrame = null;

            lock (_bufferLock)
            {
                if (_tcpBuffer.Count < 9)  // 最小帧长是9字节
                {
                    // 数据不足，等待更多数据
                    return false;
                }

                // 打印当前缓冲区状态
                var hex = HexDump(_tcpBuffer.ToArray(), 32);
                _logger.LogTrace($"TryExtractFrame 缓冲区状态 count={_tcpBuffer.Count}, data={hex}");
                System.Diagnostics.Debug.WriteLine($"[TryExtract] Buffer({_tcpBuffer.Count}): {hex}");

                // 查找帧头 AA 55
                int headerIndex = -1;
                for (int i = 0; i < _tcpBuffer.Count - 1; i++)
                {
                    if (_tcpBuffer[i] == 0xAA && _tcpBuffer[i + 1] == 0x55)
                    {
                        headerIndex = i;
                        break;
                    }
                }

                if (headerIndex < 0)
                {
                    // 找不到帧头，清理无效数据，但保留可能是帧头起点的字节
                    _logger.LogWarning($"未找到帧头，当前缓冲区={HexDump(_tcpBuffer.ToArray(), 64)}");
                    CleanInvalidData();
                    return false;
                }

                // 丢弃帧头前的无效数据
                if (headerIndex > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[WiFi RX] 丢弃帧头前 {headerIndex} 字节无效数据");
                    _tcpBuffer.RemoveRange(0, headerIndex);
                }

                // 此时帧头在索引0，检查是否有足够数据读取长度
                if (_tcpBuffer.Count < 4)
                {
                    _logger.LogTrace($"帧头已找到，但长度字段未收齐，当前缓冲区长度={_tcpBuffer.Count}");
                    return false;  // 等待更多数据
                }

                // 读取数据区长度
                ushort dataLength = (ushort)(_tcpBuffer[2] | (_tcpBuffer[3] << 8));

                // 验证长度
                if (dataLength < 2 || dataLength > 1024)
                {
                    _logger.LogWarning($"无效的数据长度: {dataLength}, 缓冲区={HexDump(_tcpBuffer.ToArray(), 64)}");
                    System.Diagnostics.Debug.WriteLine($"[WiFi RX] 无效的数据长度: {dataLength}，丢弃0xAA继续");
                    _tcpBuffer.RemoveAt(0);
                    return true;  // 继续尝试
                }

                int totalFrameLength = 2 + 2 + dataLength + 2 + 1;

                // 检查是否有完整的帧
                if (_tcpBuffer.Count < totalFrameLength)
                {
                    _logger.LogTrace($"帧不完整: 需要{totalFrameLength}, 当前{_tcpBuffer.Count}, dataLen={dataLength}, head={HexDump(_tcpBuffer.ToArray(), 64)}");
                    System.Diagnostics.Debug.WriteLine($"[WiFi RX] 帧不完整: 需要{totalFrameLength}, 当前{_tcpBuffer.Count}");
                    return false;  //  关键：等待更多数据，不清空！
                }

                // 验证帧尾
                if (_tcpBuffer[totalFrameLength - 1] != 0x0D)
                {
                    _logger.LogWarning($"帧尾错误: 期望0x0D, 实际0x{_tcpBuffer[totalFrameLength - 1]:X2}, candidate={HexDump(_tcpBuffer.Take(totalFrameLength).ToArray(), totalFrameLength)}");
                    System.Diagnostics.Debug.WriteLine($"[WiFi RX] 帧尾错误: 期望0x0D, 实际0x{_tcpBuffer[totalFrameLength - 1]:X2}");
                    // 丢弃当前帧头，继续查找
                    _tcpBuffer.RemoveAt(0);
                    return true;
                }

                // 提取完整帧
                extractedFrame = _tcpBuffer.GetRange(0, totalFrameLength).ToArray();
                _tcpBuffer.RemoveRange(0, totalFrameLength);

                _logger.LogDebug($"提取到完整帧: {DescribeFrame(extractedFrame)}");
                System.Diagnostics.Debug.WriteLine($"[WiFi RX] ✅ 提取帧成功: {extractedFrame.Length}字节, 命令=0x{extractedFrame[4]:X2}");
            }

            // 在锁外处理帧
            if (extractedFrame != null)
            {
                ProcessExtractedFrame(extractedFrame);
            }

            return true;
        }

        private void CleanInvalidData()
        {
            // 查找最后一个 0xAA
            int lastAA = -1;
            for (int i = _tcpBuffer.Count - 1; i >= 0; i--)
            {
                if (_tcpBuffer[i] == 0xAA)
                {
                    lastAA = i;
                    break;
                }
            }

            if (lastAA > 0)
            {
                // 保留从最后一个 0xAA 开始的数据
                int removeCount = lastAA;
                System.Diagnostics.Debug.WriteLine($"[WiFi RX] 清理 {removeCount} 字节无效数据");
                _tcpBuffer.RemoveRange(0, removeCount);
            }
            else if (lastAA < 0)
            {
                // 完全没有 0xAA，全部清空
                System.Diagnostics.Debug.WriteLine($"[WiFi RX] 无有效数据，清空 {_tcpBuffer.Count} 字节");
                _tcpBuffer.Clear();
            }
            // 如果 lastAA == 0，说明帧头在开头，不需要清理
        }



        /// <summary>
        /// 处理提取到的帧
        /// </summary>
        private void ProcessExtractedFrame(byte[] frame)
        {
            try
            {
                _logger.LogDebug($"开始处理接收帧: {DescribeFrame(frame)}");
                DataReceived?.Invoke(this, frame);
                var previousTrackLatency = _trackLatencyForCurrentFeed.Value;
                _trackLatencyForCurrentFeed.Value = true;
                try
                {
                    _parser.FeedData(frame);
                }
                finally
                {
                    _trackLatencyForCurrentFeed.Value = previousTrackLatency;
                }
                _logger.LogTrace($"已喂入协议解析器: 命令=0x{frame[4]:X2}, 长度={frame.Length}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"处理接收数据/触发事件时异常, frame={DescribeFrame(frame)}");
            }
        }

        private int _isHeartbeating = 0;

        /// <summary>
        /// 快速网络接口存活检测（每3秒），直接查询 TCP 连接所在网卡的 OperationalStatus。
        /// <para>WiFi 断开时网卡状态会立即从 Up 变为 Down，比 Socket.Poll 和 TCP 超时快得多。</para>
        /// </summary>
        private void CheckNetworkInterfaceAlive()
        {
            if (!IsConnected) return;
            var localAddr = _localBindAddress;
            if (localAddr == null) return;

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 只需检查包含当前连接 IP 的接口是否还在运行
                    bool ownsIp = false;
                    foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.Equals(localAddr))
                        {
                            ownsIp = true;
                            break;
                        }
                    }
                    if (!ownsIp) continue;

                    if (ni.OperationalStatus != OperationalStatus.Up)
                    {
                        _logger.LogWarning("WiFi 网卡状态变更: {Status}，触发断联", ni.OperationalStatus);
                        DevicePhysicallyDisconnected?.Invoke(this, $"网络接口已断开（{ni.Name}: {ni.OperationalStatus}）");
                        _ = Task.Run(async () => await DisconnectAsync());
                    }
                    return; // 找到匹配接口，检查完毕
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "网络接口检测异常");
            }
        }

        private async Task SendHeartbeatAsync()
        {
            if (!IsConnected) return;

            // 防止并行发送导致心跳长期积压在发送锁上
            if (System.Threading.Interlocked.CompareExchange(ref _isHeartbeating, 1, 0) != 0) return;

            try
            {


                // 如果上一轮心跳还没收到任何设备数据，则累加未响应次数；
                // 只标记离线，不主动关闭 TCP 连接。
                if (System.Threading.Interlocked.CompareExchange(ref _awaitingHeartbeatResponse, 0, 0) == 1)
                {
                    var missed = System.Threading.Interlocked.Increment(ref _missedHeartbeatChecks);
                    if (missed >= HEARTBEAT_MISS_THRESHOLD)
                    {
                        SetDeviceConnectionState(false, $"[警告] 连续 {missed} 次心跳未收到设备回应，设备离线");
                        // 触发物理断开事件，UI 弹出重连弹窗
                        DevicePhysicallyDisconnected?.Invoke(this, $"心跳超时：连续 {missed} 次未收到设备回应");
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
                _logger.LogError(ex, "心跳发送异常");
                // TCP 发送失败意味着连接已彻底断开
                if (IsConnected)
                {
                    _ = Task.Run(async () => await DisconnectAsync());
                    DevicePhysicallyDisconnected?.Invoke(this, "WiFi 连接已断开（TCP 发送失败）");
                }
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _isHeartbeating, 0);
            }
        }

        private void OnParsedFrameReceived(ParsedFrame frame)
        {
            try
            {
                // 这里以“TCP 入站帧”为准处理业务数据，避免设备端方向字节与上位机约定不一致时
                // 导致帧已解析成功但业务事件不触发。
                if (!frame.IsFromDevice)
                {
                    _logger.LogWarning($"收到方向字节异常的帧，仍按入站帧处理: Command=0x{frame.Command:X2}, Direction=0x{frame.Direction:X2}");
                }

                System.Diagnostics.Debug.WriteLine($"=== 收到帧数据 ===");
                System.Diagnostics.Debug.WriteLine($"时间: {DateTime.Now:HH:mm:ss.fff}");
                System.Diagnostics.Debug.WriteLine($"命令: 0x{frame.Command:X2}");
                System.Diagnostics.Debug.WriteLine($"方向: {(frame.IsToDevice ? "上位机->设备" : "设备->上位机")}");
                System.Diagnostics.Debug.WriteLine($"数据长度: {frame.Data?.Length ?? 0} 字节");

                // 只要收到任何有效协议帧，就说明链路存活，更新时间
                lock (_timeLock)
                {
                    _lastReceiveTime = DateTime.Now;
                }

                System.Threading.Interlocked.Exchange(ref _missedHeartbeatChecks, 0);
                System.Threading.Interlocked.Exchange(ref _deviceConnected, 1);

                // 依据当前接收通道选择 RTT 计算方式：TCP 心跳、UDP 时间戳
                UpdateLatencyForIncomingFrame(frame);

                ParseAndRaiseDataEvent(frame);
                UpdatePacketLoss();
                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理解析帧失败");
            }
        }

        private void ParseAndRaiseDataEvent(ParsedFrame frame)
        {
            try
            {
                switch (frame.Command)
                {
                    case ProtocolCommandCode.PressureData:
                        var pressureData = DataParser.ParsePressureData(frame.Data);
                        PressureDataReceived?.Invoke(this, pressureData);
                        _logger.LogDebug($"收到压力数据: {pressureData.Items.Count} 路");
                        _logger.LogInformation($"收到压力数据: {pressureData.Items.Count} 路");
                        break;

                    case ProtocolCommandCode.EdemaData:
                        var edemaData = DataParser.ParseEdemaData(frame.Data);
                        EdemaDataReceived?.Invoke(this, edemaData);
                        _logger.LogDebug($"收到水肿数据: 传感器{edemaData.SensorId}, 百分比={edemaData.EdemaPercent:F1}%");
                        _logger.LogInformation($"收到水肿数据: 传感器{edemaData.SensorId}, 百分比={edemaData.EdemaPercent:F1}%");

                        break;

                    case ProtocolCommandCode.MotorStatus:
                        var motorStatus = DataParser.ParseMotorStatus(frame.Data);
                        MotorStatusReceived?.Invoke(this, motorStatus);
                        _logger.LogDebug($"收到电机状态: 步进1={motorStatus.Stepper1State}, 步进2={motorStatus.Stepper2State}");
                        _logger.LogInformation($"收到电机状态: 步进1={motorStatus.Stepper1State}, 步进2={motorStatus.Stepper2State}");

                        break;

                    case ProtocolCommandCode.TherapyProgress:
                        var progress = DataParser.ParseTherapyProgress(frame.Data);
                        TherapyProgressReceived?.Invoke(this, progress);
                        _logger.LogDebug($"收到治疗进度: {progress.Progress}%, 剩余{progress.RemainingTime}秒");
                        _logger.LogInformation($"收到治疗进度: {progress.Progress}%, 剩余{progress.RemainingTime}秒");
                        break;

                    case ProtocolCommandCode.MotorPosition:
                        var motorPosition = DataParser.ParseMotorPosition(frame.Data);
                        MotorPositionReceived?.Invoke(this, motorPosition);
                        _logger.LogDebug($"收到电机位置: 2号={motorPosition.Motor2EncoderValue}, 4号={motorPosition.Motor4EncoderValue}");
                        break;

                    case ProtocolCommandCode.Ack:
                    case ProtocolCommandCode.Nak:
                        var response = DataParser.ParseGeneralResponse(frame.Data);
                        response.Result = (byte)(frame.Command == ProtocolCommandCode.Ack ? 0 : 1);
                        ResponseReceived?.Invoke(this, response);
                        _logger.LogDebug($"收到响应: {(response.Result == 0 ? "成功" : "失败")}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"解析数据失败: Command=0x{frame.Command:X2}");
            }
        }

        private void UpdatePacketLoss()
        {
            int total = _packetsSent + _packetsLost;
            _packetLoss = total > 0 ? Math.Round((double)_packetsLost / total * 100, 2) : 0;
        }

        private void UpdateConnectionState(ConnectionState state)
        {
            _connectionState = state;
            _logger.LogInformation($"WiFi 状态变更: {state}");
            UpdateConnectionStatus();
        }

        private void UpdateConnectionStatus()
        {
            var udpPacketsReceived = System.Threading.Interlocked.CompareExchange(ref _udpPacketsReceived, 0, 0);
            var udpPacketsDropped = System.Threading.Interlocked.CompareExchange(ref _udpPacketsDropped, 0, 0);

            var status = new SystemStatus
            {
                WifiState = _connectionState,
                IsDeviceConnected = IsDeviceConnected,
                Latency = _latency,
                TcpLatency = _latency,
                UdpLatency = _udpLatency,
                SerialLatency = 0,
                IsSerialTransport = false,
                PacketLoss = _packetLoss,
                UdpPacketsReceived = udpPacketsReceived,
                UdpPacketsDropped = udpPacketsDropped,
                LastLog = _connectionState switch
                {
                    ConnectionState.Connected when IsDeviceConnected => $"TCP延迟: {_latency:F0}ms, UDP延迟: {_udpLatency:F0}ms, UDP收到{udpPacketsReceived}包, 丢包{udpPacketsDropped}",
                    ConnectionState.Connected => $"WiFi 已连接，设备离线，TCP延迟: {_latency:F0}ms, UDP延迟: {_udpLatency:F0}ms, UDP收到{udpPacketsReceived}包, 丢包{udpPacketsDropped}, 等待心跳恢复",
                    ConnectionState.Connecting => "WiFi 连接中...",
                    ConnectionState.Error => "WiFi 连接错误",
                    ConnectionState.Disconnected => "WiFi 未连接",
                    _ => "WiFi 未连接"
                }
            };

            SystemStatusChanged?.Invoke(this, status);
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

        private static bool ShouldUseUdp(byte command)
        {
            return command switch
            {
                ProtocolCommandCode.PressureData => true,
                ProtocolCommandCode.EdemaData => true,
                ProtocolCommandCode.MotorStatus => true,
                ProtocolCommandCode.TherapyProgress => true,
                _ => false
            };
        }
    }
}
