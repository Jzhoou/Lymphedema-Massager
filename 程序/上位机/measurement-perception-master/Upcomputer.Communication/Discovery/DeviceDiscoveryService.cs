using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Upcomputer.Communication.Protocol;
using Upcomputer.Core.Models;

namespace Upcomputer.Communication.Discovery
{
    /// <summary>
    /// 设备发现服务（UDP广播扫描）
    /// </summary>
    public class DeviceDiscoveryService : IDisposable
    {
        private const int DISCOVERY_PORT = 9527;
        private readonly ILogger<DeviceDiscoveryService>? _logger;
        private CancellationTokenSource? _scanCancellationSource;
        private bool _isScanning;
        private bool _disposed;

        /// <summary>
        /// 发现设备时触发
        /// </summary>
        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

        /// <summary>
        /// 扫描完成时触发
        /// </summary>
        public event EventHandler? ScanCompleted;

        public DeviceDiscoveryService(ILogger<DeviceDiscoveryService>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// 开始扫描设备
        /// </summary>
        public async Task<List<DiscoveredDevice>> ScanAsync(int timeoutMs = 5000)
        {
            if (_isScanning)
            {
                _logger?.LogWarning("扫描已在进行中");
                return new List<DiscoveredDevice>();
            }

            _isScanning = true;
            var devices = new List<DiscoveredDevice>();
            _scanCancellationSource = new CancellationTokenSource(timeoutMs);
            var cancellation = _scanCancellationSource;

            UdpClient? udpSend = null;
            UdpClient? udpReceive = null;

            try
            {
                IPAddress? localBindIp = NetworkHelper.GetPhysicalWifiIpAddress(_logger);

                udpReceive = new UdpClient();
                udpReceive.Client.Bind(new IPEndPoint(IPAddress.Any, DISCOVERY_PORT));
                udpReceive.EnableBroadcast = true;

                udpSend = new UdpClient();
                udpSend.EnableBroadcast = true;

                if (localBindIp != null)
                {
                    udpSend = new UdpClient(new IPEndPoint(localBindIp, 0));
                    _logger?.LogInformation($"已绑定物理网卡: {localBindIp}");
                }
                else
                {
                    udpSend = new UdpClient();
                    _logger?.LogWarning("未找到物理网卡，使用默认网络配置");
                }
                udpSend.EnableBroadcast = true;

                byte[] queryData = new byte[] { 0xAA, 0x55, 0x02, 0x00, 0x04, 0x00, 0x03, 0x70, 0x0D };

                _logger?.LogInformation($"发送发现请求: {BitConverter.ToString(queryData)}");

                await udpSend.SendAsync(queryData, queryData.Length, new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT));

                _logger?.LogInformation("已广播设备发现请求");

                while (!cancellation.Token.IsCancellationRequested && _isScanning)
                {
                    try
                    {
                        var result = await udpReceive.ReceiveAsync(cancellation.Token);

                        if (result.Buffer.Length == 9)
                        {
                            _logger?.LogDebug("忽略自己发送的发现请求");
                            continue;
                        }

                        if (TryParseDiscoveredDevice(result.Buffer, out var device))
                        {
                            if (!devices.Any(d => d.IpAddress == device.IpAddress))
                            {
                                devices.Add(device);
                                _logger?.LogInformation("发现设备: {Device}", device.DisplayText);
                                DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device));
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger?.LogInformation("扫描超时结束");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "接收异常");
                    }
                }

                _logger?.LogInformation($"扫描完成，共发现 {devices.Count} 台设备");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "扫描失败");
            }
            finally
            {
                _isScanning = false;
                _scanCancellationSource?.Dispose();
                _scanCancellationSource = null;

                // 安全释放
                udpSend?.Close();
                udpReceive?.Close();
                udpSend?.Dispose();
                udpReceive?.Dispose();

                ScanCompleted?.Invoke(this, EventArgs.Empty);
            }

            return devices;
        }

        /// <summary>
        /// 开始扫描设备
        /// </summary>
        public Task StartScanAsync(int timeoutMs = 3000)
        {
            return ScanAsync(timeoutMs);
        }

        private static bool TryParseDiscoveredDevice(byte[] data, out DiscoveredDevice device)
        {
            device = new DiscoveredDevice();

            if (data.Length < 12 || data[0] != 0xAA || data[1] != 0x55 || data[4] != 0x05)
            {
                return false;
            }

            if (data.Length >= 10)
            {
                device.IpAddress = $"{data[6]}.{data[7]}.{data[8]}.{data[9]}";
            }

            if (data.Length >= 12)
            {
                device.Port = data[10] | (data[11] << 8);
            }

            if (data.Length >= 27)
            {
                device.Name = Encoding.ASCII.GetString(data, 12, 15).TrimEnd('\0');
            }

            if (data.Length >= 42)
            {
                device.Version = Encoding.ASCII.GetString(data, 27, 15).TrimEnd('\0');
            }

            device.DiscoverTime = DateTime.Now;

            return true;
        }

        /// <summary>
        /// 停止扫描
        /// </summary>
        public void StopScan()
        {
            _isScanning = false;
            _scanCancellationSource?.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopScan();
            _scanCancellationSource?.Dispose();
            _scanCancellationSource = null;
        }
    }



    public class DiscoveredDevice
    {
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Version { get; set; } = string.Empty;
        public DateTime DiscoverTime { get; set; }

        public string DisplayText => $"{Name} ({IpAddress}:{Port}) - {Version}";
    }

    public class DeviceDiscoveredEventArgs : EventArgs
    {
        public DiscoveredDevice Device { get; }
        public DeviceDiscoveredEventArgs(DiscoveredDevice device) => Device = device;
    }

    internal static class NetworkHelper
    {
        /// <summary>
        /// 智能获取物理 WiFi 或 以太网网卡的 IP 地址 (自动过滤虚拟网卡)
        /// </summary>
        internal static IPAddress? GetPhysicalWifiIpAddress(ILogger? logger = null)
        {
            try
            {
                var nicList = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up) // 必须是“已连接”
                    .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback) // 排除回环
                    .ToList();

                // 按优先级排序：WiFi > 以太网 > 其他
                var sortedNics = nicList
                    .OrderByDescending(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    .ThenByDescending(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                    .ThenByDescending(nic => nic.GetIPProperties().GatewayAddresses.Count) // 有网关的优先 (通常是能上网的那个)
                    .ToList();

                foreach (var nic in sortedNics)
                {
                    // 【关键过滤】排除虚拟网卡
                    if (IsVirtualNic(nic))
                    {
                        logger?.LogDebug($"跳过虚拟网卡: {nic.Name} ({nic.Description})");
                        continue;
                    }

                    // 获取 IPv4 地址
                    var ipProps = nic.GetIPProperties();
                    var ipv4Addr = ipProps.UnicastAddresses
                        .FirstOrDefault(addr =>
                            addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(addr.Address) &&
                            !addr.Address.ToString().StartsWith("169.254.") // 排除 APIPA 自动私有地址
                        );

                    if (ipv4Addr != null)
                    {
                        logger?.LogInformation($"选中物理网卡: {nic.Name}");
                        return ipv4Addr.Address;
                    }
                }

                logger?.LogWarning("未找到合适的物理网卡");
                return null;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "枚举网卡失败");
                return null;
            }
        }

        /// <summary>
        /// 根据描述/名称判断是否为虚拟网卡
        /// </summary>
        private static bool IsVirtualNic(NetworkInterface nic)
        {
            string desc = (nic.Description + " " + nic.Name).ToLower();

            // 虚拟网卡关键词黑名单
            string[] keywords = {
                "vmware", "virtualbox", "hyper-v", "wsl", "tap-windows",
                "windscribe", "expressvpn", "nordvpn", "zerotier",
                "pseudo-interface", "loopback", "teredo", "isatap",
                "bluetooth", "microsoft wi-fi direct"
            };

            return keywords.Any(k => desc.Contains(k));
        }
    }

    }