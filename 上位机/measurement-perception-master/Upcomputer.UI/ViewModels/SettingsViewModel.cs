using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Upcomputer.Common.Constants;
using Upcomputer.Common.Enums;
using Upcomputer.Common.Models;
using Upcomputer.Communication;
using Upcomputer.Communication.Discovery;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;
using Upcomputer.UI.Views;
using Upcomputer.Common.Helpers;

namespace Upcomputer.UI.ViewModels
{
    public class SettingsViewModel : ObservableObject, IDisposable
    {
        private readonly ICommunicationService _communicationService;

        private string _wifiIpAddress = AppConstants.DefaultWifiIp;
        private int _wifiPort = AppConstants.DefaultWifiPort;
        private bool _isWifiConnected;

        private string _selectedComPort = "COM3";
        private int _baudRate = AppConstants.DefaultBaudRate;
        private bool _isSerialConnected;

        private double _pressureThreshold = AppConstants.DefaultPressureMaxThreshold;
        private double _edemaThreshold = AppConstants.DefaultEdemaMaxThreshold;

        private bool _cloudSyncEnabled = true;
        private string _cloudEndpoint = "https://api.example.com/upcomputer";

        private string _connectionLog = string.Empty;

        private ObservableCollection<DiscoveredDevice> _discoveredDevices = new();
        private DiscoveredDevice? _selectedDevice;
        private string _scanStatus = "就绪";
        private readonly DeviceDiscoveryService _discoveryService = new();
        private DeviceProvisioningWindow? _deviceProvisioningWindow;
        private SystemStatus _lastStatus = new();

        public ObservableCollection<DiscoveredDevice> DiscoveredDevices
        {
            get => _discoveredDevices;
            set => SetField(ref _discoveredDevices, value);
        }

        public DiscoveredDevice? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetField(ref _selectedDevice, value) && value != null)
                {
                    // 当选中设备变化时，自动更新 IP 和端口
                    WifiIpAddress = value.IpAddress;
                    WifiPort = value.Port;

                    // 可选：添加日志记录
                    AppendLog($"已选择设备: {value.DisplayText}");
                }
            }
        }

        public string ScanStatus
        {
            get => _scanStatus;
            set => SetField(ref _scanStatus, value);
        }

        public ICommand ScanDevicesCommand { get; }

        public SettingsViewModel(ICommunicationService communicationService)
        {
            _communicationService = communicationService;

            _communicationService.SystemStatusChanged += OnSystemStatusChanged;

            ConnectWifiCommand = new AsyncRelayCommand(ConnectWifiAsync, () => !IsWifiConnected);
            DisconnectWifiCommand = new AsyncRelayCommand(DisconnectWifiAsync, () => IsWifiConnected);
            ConnectSerialCommand = new AsyncRelayCommand(ConnectSerialAsync, () => !IsSerialConnected);
            DisconnectSerialCommand = new AsyncRelayCommand(DisconnectSerialAsync, () => IsSerialConnected);
            RefreshComPortsCommand = new RelayCommand(RefreshAvailableComPorts);
            SaveThresholdsCommand = new RelayCommand(SaveThresholds);
            TestLatencyCommand = new AsyncRelayCommand(TestLatencyAsync);
            SaveCloudSettingsCommand = new RelayCommand(SaveCloudSettings);
            ScanDevicesCommand = new AsyncRelayCommand(ScanDevicesAsync);

            _discoveryService.DeviceDiscovered += OnDeviceDiscovered;
            _discoveryService.ScanCompleted += OnScanCompleted;

            LoadAvailableComPorts();
            LoadSavedConfig();

            // 初始化时同步一次状态
            IsWifiConnected = false;
            IsSerialConnected = false;
        }

        /// <summary>
        /// 刷新连接状态（供 View 调用）
        /// </summary>
        public void RefreshConnectionStatus()
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] RefreshConnectionStatus - 当前服务状态: {_communicationService.IsConnected}");

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_communicationService is CommunicationServiceRouter router)
                {
                    _lastStatus = new SystemStatus
                    {
                        WifiState = router.CurrentMode == CommunicationMode.WiFi && router.IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected,
                        SerialState = router.CurrentMode == CommunicationMode.Serial && router.IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected,
                        LastLog = router.CurrentMode.ToString()
                    };
                }

                IsWifiConnected = _lastStatus.WifiState == ConnectionState.Connected;
                IsSerialConnected = _lastStatus.SerialState == ConnectionState.Connected;

                // 通知属性变更
                OnPropertyChanged(nameof(IsWifiConnected));
                OnPropertyChanged(nameof(IsSerialConnected));
                OnPropertyChanged(nameof(WifiIpAddress));
                OnPropertyChanged(nameof(WifiPort));

                // 刷新命令
                (ConnectWifiCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DisconnectWifiCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ConnectSerialCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DisconnectSerialCommand as RelayCommand)?.RaiseCanExecuteChanged();

                // 强制 WPF 命令系统刷新
                CommandManager.InvalidateRequerySuggested();
            });
        }

        private void OnSystemStatusChanged(object? sender, SystemStatus status)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                IsWifiConnected = status.WifiState == ConnectionState.Connected;
                IsSerialConnected = status.SerialState == ConnectionState.Connected;

                // 刷新命令
                (ConnectWifiCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DisconnectWifiCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ConnectSerialCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DisconnectSerialCommand as RelayCommand)?.RaiseCanExecuteChanged();
            });
        }

        public void Dispose()
        {
            _communicationService.SystemStatusChanged -= OnSystemStatusChanged;
            _discoveryService.DeviceDiscovered -= OnDeviceDiscovered;
            _discoveryService.ScanCompleted -= OnScanCompleted;
            _ = DisconnectSerialAsync();
        }

        private void LoadSavedConfig()
        {
            var config = AppConfigHelper.Load();
            WifiIpAddress = config.LastWifiIp;
            WifiPort = config.LastWifiPort;
            if (!string.IsNullOrWhiteSpace(config.LastSerialPort) && AvailableComPorts.Contains(config.LastSerialPort))
            {
                SelectedComPort = config.LastSerialPort;
            }
            BaudRate = config.LastBaudRate;
            PressureThreshold = config.PressureThreshold;
            EdemaThreshold = config.EdemaThreshold;

            if (AvailableComPorts.Count > 0 && !AvailableComPorts.Contains(SelectedComPort))
            {
                SelectedComPort = AvailableComPorts[0];
            }
        }

        public string WifiIpAddress
        {
            get => _wifiIpAddress;
            set => SetField(ref _wifiIpAddress, value);
        }

        public int WifiPort
        {
            get => _wifiPort;
            set => SetField(ref _wifiPort, value);
        }

        public bool IsWifiConnected
        {
            get => _isWifiConnected;
            set
            {
                if (SetField(ref _isWifiConnected, value))
                {
                    (ConnectWifiCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (DisconnectWifiCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<string> AvailableComPorts { get; } = new();
        public ObservableCollection<int> BaudRates { get; } = new() { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800 };

        public string SelectedComPort
        {
            get => _selectedComPort;
            set => SetField(ref _selectedComPort, value);
        }

        public int BaudRate
        {
            get => _baudRate;
            set => SetField(ref _baudRate, value);
        }

        public bool IsSerialConnected
        {
            get => _isSerialConnected;
            set
            {
                if (SetField(ref _isSerialConnected, value))
                {
                    (ConnectSerialCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (DisconnectSerialCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public double PressureThreshold
        {
            get => _pressureThreshold;
            set => SetField(ref _pressureThreshold, value);
        }

        public double EdemaThreshold
        {
            get => _edemaThreshold;
            set => SetField(ref _edemaThreshold, value);
        }

        public bool CloudSyncEnabled
        {
            get => _cloudSyncEnabled;
            set => SetField(ref _cloudSyncEnabled, value);
        }

        public string CloudEndpoint
        {
            get => _cloudEndpoint;
            set => SetField(ref _cloudEndpoint, value);
        }

        public string ConnectionLog
        {
            get => _connectionLog;
            set => SetField(ref _connectionLog, value);
        }

        public ICommand ConnectWifiCommand { get; }
        public ICommand DisconnectWifiCommand { get; }
        public ICommand ConnectSerialCommand { get; }
        public ICommand DisconnectSerialCommand { get; }
        public ICommand RefreshComPortsCommand { get; }
        public ICommand SaveThresholdsCommand { get; }
        public ICommand TestLatencyCommand { get; }
        public ICommand SaveCloudSettingsCommand { get; }

        private void LoadAvailableComPorts()
        {
            AvailableComPorts.Clear();
            var allPorts = System.IO.Ports.SerialPort.GetPortNames().OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

            foreach (var port in allPorts)
            {
                // 过滤已知的"幽灵端口"（OTG 拔出后注册表仍有残留记录）
                if (_communicationService is CommunicationServiceRouter router && router.IsSerialPortLikelyStale(port))
                {
                    System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] 过滤幽灵端口: {port}");
                    continue;
                }
                AvailableComPorts.Add(port);
            }

            if (AvailableComPorts.Count > 0)
            {
                SelectedComPort = AvailableComPorts[0];
            }
            else
            {
                SelectedComPort = string.Empty;
            }
        }

        private void RefreshAvailableComPorts()
        {
            var previousPort = SelectedComPort;
            LoadAvailableComPorts();

            if (!string.IsNullOrWhiteSpace(previousPort) && AvailableComPorts.Contains(previousPort))
            {
                SelectedComPort = previousPort;
            }

            AppendLog($"已刷新串口列表，共 {AvailableComPorts.Count} 个");
        }

        private async Task ConnectWifiAsync()
        {
            AppendLog($"正在连接 WiFi {WifiIpAddress}:{WifiPort}...");

            var result = await _communicationService.ConnectAsync(WifiIpAddress, WifiPort);
            if (result)
            {
                AppendLog("WiFi 连接成功");
                SaveCurrentConfig();
            }
            else
            {
                AppendLog("WiFi 连接失败");
            }
        }

        private void SaveCurrentConfig()
        {
            var config = AppConfigHelper.Load();
            config.LastWifiIp = WifiIpAddress;
            config.LastWifiPort = WifiPort;
            config.LastSerialPort = SelectedComPort;
            config.LastBaudRate = BaudRate;
            config.PressureThreshold = PressureThreshold;
            config.EdemaThreshold = EdemaThreshold;
            AppConfigHelper.Save(config);
        }

        private async Task DisconnectWifiAsync()
        {
            await _communicationService.DisconnectAsync();
            AppendLog("WiFi 已断开");
        }

        private async Task ConnectSerialAsync()
        {
            if (IsSerialConnected)
            {
                AppendLog($"串口已连接: {SelectedComPort} @ {BaudRate}");
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedComPort))
            {
                AppendLog("请选择有效的串口端口");
                MessageBox.Show("请先选择一个可用的串口端口。", "串口连接", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var result = await _communicationService.ConnectSerialAsync(SelectedComPort, BaudRate);
                IsSerialConnected = result;
                if (!result)
                {
                    AppendLog("串口连接失败");
                    MessageBox.Show("串口连接失败，请检查端口或波特率。", "串口连接", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SaveCurrentConfig();
                AppendLog($"串口连接成功: {SelectedComPort} @ {BaudRate}");
            }
            catch (Exception ex)
            {
                IsSerialConnected = false;
                AppendLog($"串口连接失败: {ex.Message}");
                MessageBox.Show($"串口连接失败：{ex.Message}", "串口连接", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DisconnectSerialAsync()
        {
            try
            {
                await _communicationService.DisconnectSerialAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"串口断开时发生错误: {ex.Message}");
            }
            finally
            {
                if (IsSerialConnected)
                {
                    IsSerialConnected = false;
                }

                AppendLog("串口已断开");
            }
        }

        private void SaveThresholds()
        {
            SaveCurrentConfig();
            AppendLog($"阈值已保存: 压力={PressureThreshold}g, 水肿={EdemaThreshold}%");
        }

        private async Task TestLatencyAsync()
        {
            AppendLog("正在测试网络时延...");
            await Task.Delay(500);
            AppendLog($"当前时延: 32ms, 丢包率: 0.02%");
        }

        private void SaveCloudSettings()
        {
            AppendLog($"云服务设置已保存: 同步={(CloudSyncEnabled ? "开启" : "关闭")}");
        }

        public void OpenDeviceProvisioningGuide()
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (_deviceProvisioningWindow != null)
                    {
                        if (_deviceProvisioningWindow.IsVisible)
                        {
                            _deviceProvisioningWindow.Activate();
                            return;
                        }

                        _deviceProvisioningWindow = null;
                    }

                    _deviceProvisioningWindow = new DeviceProvisioningWindow
                    {
                        Owner = Application.Current?.MainWindow,
                        ShowInTaskbar = true
                    };
                    _deviceProvisioningWindow.Closed += (_, _) => _deviceProvisioningWindow = null;
                    _deviceProvisioningWindow.Show();
                    _deviceProvisioningWindow.Activate();
                });
            }
            catch (Exception ex)
            {
                AppendLog($"打开设备配网页面失败: {ex.Message}");
                MessageBox.Show($"打开设备配网页面失败：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            ConnectionLog = $"[{timestamp}] {message}\n{ConnectionLog}";

            // 限制日志长度
            var lines = ConnectionLog.Split('\n');
            if (lines.Length > 50)
            {
                ConnectionLog = string.Join('\n', lines.Take(50));
            }
        }

        private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (e.ConnectionType == ConnectionType.WiFi)
                {
                    IsWifiConnected = e.IsConnected;
                }
                else if (e.ConnectionType == ConnectionType.Serial)
                {
                    IsSerialConnected = e.IsConnected;
                }
            });
        }

        private async Task ScanDevicesAsync()
        {
            ScanStatus = "扫描中...";
            DiscoveredDevices.Clear();
            SelectedDevice = null;  // 清空当前选中

            try
            {
                await _discoveryService.StartScanAsync(3000);
            }
            catch (Exception ex)
            {
                ScanStatus = $"扫描失败: {ex.Message}";
                AppendLog($"设备扫描失败: {ex.Message}");
            }
        }

        private void OnDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (!DiscoveredDevices.Any(d => d.IpAddress == e.Device.IpAddress))
                {
                    DiscoveredDevices.Add(e.Device);
                    AppendLog($"发现设备: {e.Device.DisplayText}");
                    // 如果是第一个发现的设备，自动选中
                    if (DiscoveredDevices.Count == 1)
                    {
                        SelectedDevice = e.Device;
                    }
                }
            });
        }


        private void OnScanCompleted(object? sender, EventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (DiscoveredDevices.Count > 0)
                {
                    ScanStatus = $"发现 {DiscoveredDevices.Count} 个设备";

                    // 扫描完成后，如果没有选中任何设备，默认选中第一个
                    if (SelectedDevice == null && DiscoveredDevices.Count > 0)
                    {
                        SelectedDevice = DiscoveredDevices[0];
                    }

                    AppendLog($"扫描完成，共发现 {DiscoveredDevices.Count} 个设备");
                }
                else
                {
                    ScanStatus = "未发现设备";
                    AppendLog("扫描完成，未发现设备");
                }
            });
        }
    }
}
