using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Interop;
using System.IO.Ports;
using Upcomputer.Common.Constants;
using Upcomputer.Common.Enums;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;
using Upcomputer.Core.Services;
using Upcomputer.Communication.Protocol;
using Upcomputer.UI.ViewModels;
using Upcomputer.Communication;
using Upcomputer.UI.Views;

namespace Upcomputer.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly IServiceProvider _serviceProvider;

        // 缓存视图实例
        private UserInterfaceView? _userInterfaceView;
        private UserManagementView? _userManagementView;
        private DashboardView? _dashboardView;
        private ControlPanelView? _controlPanelView;
        private HistoryView? _historyView;
        private SettingsView? _settingsView;

        private Border? _wifiIndicator;
        private Border? _serialIndicator;
        private TextBlock? _statusSummaryText;
        private TextBlock? _timeText;
        private Frame? _contentFrame;
        private StackPanel? _navigationPanel;
        private Grid? _rootGrid;
        private Border? _reconnectOverlay;
        private TextBlock? _reconnectStatusText;
        private Border? _pressureAlertOverlay;
        private SettingsViewModel? _settingsViewModel;
        private bool _isSerialConnected;
        private ICommunicationService? _communicationService;
        private readonly SerialCommunication _serialCommunication;
        private readonly WifiCommunication _wifiCommunication;
        private CancellationTokenSource? _reconnectCancellation;
        private bool _reconnectSuppressed;
        private bool _isPressureAlertVisible;
        private bool _isReconnectOverlayVisible;
        private IntPtr _windowHandle;
        private HwndSource? _hwndSource;

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
        {
            _viewModel = viewModel;
            _serviceProvider = serviceProvider;
            _communicationService = _serviceProvider.GetRequiredService<ICommunicationService>();
            _serialCommunication = _serviceProvider.GetRequiredService<SerialCommunication>();
            _wifiCommunication = _serviceProvider.GetRequiredService<WifiCommunication>();
            DataContext = _viewModel;

            _communicationService.SystemStatusChanged += OnCommunicationSystemStatusChanged;
            _communicationService.PressureDataReceived += OnPressureDataReceived;
            _serialCommunication.DevicePhysicallyDisconnected += OnDevicePhysicallyDisconnected;
            _wifiCommunication.DevicePhysicallyDisconnected += OnWifiPhysicallyDisconnected;

            BuildUserInterface();
            BindViewModelEvents();
            SourceInitialized += OnSourceInitialized;
            Closing += OnClosing;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            _hwndSource = source;
            _windowHandle = source.Handle;
            source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                if (wParam == (IntPtr)DBT_DEVICEREMOVECOMPLETE)
                {
                    HandleDeviceRemoval();
                }
                else if (wParam == (IntPtr)DBT_DEVICEARRIVAL)
                {
                    HandleDeviceArrival();
                }
            }

            return IntPtr.Zero;
        }

        private void HandleDeviceRemoval()
        {
            if (IsCurrentSerialPortDisconnected())
            {
                Application.Current?.Dispatcher.Invoke(() => BeginReconnect(CommunicationMode.Serial, _viewModel.SystemStatus));
            }
        }

        private void HandleDeviceArrival()
        {
            if (_isReconnectOverlayVisible && IsCurrentSerialPortAvailable())
            {
                _ = Task.Run(async () =>
                {
                    var reconnectConfig = GetReconnectConfig();
                    try
                    {
                        await _communicationService!.ConnectSerialAsync(reconnectConfig.SerialPortName, reconnectConfig.BaudRate);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"立即重连失败：{ex}");
                    }
                });
            }
        }

        private bool IsCurrentSerialPortDisconnected() => _serialCommunication.IsConnected && !_serialCommunication.IsConnectedPortPresent;

        private bool IsCurrentSerialPortAvailable() => _serialCommunication.IsConnectedPortPresent;

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            try
            {
                var treatmentEngine = _serviceProvider.GetRequiredService<TreatmentEngine>();
                var treatmentRepository = _serviceProvider.GetRequiredService<ITreatmentRepository>();

                if (treatmentEngine.CurrentSession is { IsRunning: true } session && session.SessionId != Guid.Empty)
                {
                    treatmentEngine.StopTreatment();
                    Task.Run(() => treatmentRepository.EndTreatmentSessionAsync(session.SessionId, session.EndTime ?? DateTime.Now))
                        .GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"窗口关闭时结束治疗会话失败：{ex}");
            }

            _viewModel.DashboardViewModel?.Dispose();
            _viewModel.ControlPanelViewModel?.Dispose();
            _viewModel.Dispose();
            Closing -= OnClosing;
        }

        private void BuildUserInterface()
        {
            Title = AppConstants.AppName;
            Width = 1400;
            Height = 900;
            MinWidth = 1200;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(15, 25, 35));

            _rootGrid = new Grid();
            _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

            // 顶部标题栏
            var titleBar = CreateTitleBar();
            Grid.SetRow(titleBar, 0);
            _rootGrid.Children.Add(titleBar);

            // 中间主区域
            var mainPanel = new Grid();
            mainPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            mainPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(mainPanel, 1);

            // 左侧导航栏
            _navigationPanel = CreateNavigationPanel();
            Grid.SetColumn(_navigationPanel, 0);
            mainPanel.Children.Add(_navigationPanel);

            // 右侧内容区
            _contentFrame = new Frame
            {
                Background = new SolidColorBrush(Color.FromRgb(25, 35, 45))
            };
            Grid.SetColumn(_contentFrame, 1);
            mainPanel.Children.Add(_contentFrame);

            Grid.SetRow(mainPanel, 1);
            _rootGrid.Children.Add(mainPanel);

            // 底部状态栏
            var statusBar = CreateStatusBar();
            Grid.SetRow(statusBar, 2);
            _rootGrid.Children.Add(statusBar);

            _reconnectOverlay = CreateReconnectOverlay();
            Grid.SetRowSpan(_reconnectOverlay, 3);
            _rootGrid.Children.Add(_reconnectOverlay);

            _pressureAlertOverlay = CreatePressureAlertOverlay();
            Grid.SetRowSpan(_pressureAlertOverlay, 3);
            _rootGrid.Children.Add(_pressureAlertOverlay);

            Content = _rootGrid;

            // 默认显示仪表盘
            NavigateTo("UserInterface");

            _contentFrame.NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden;

        }

        private Border CreateReconnectOverlay()
        {
            var overlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(170, 8, 16, 24)),
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var panel = new Border
            {
                Width = 430,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromRgb(24, 34, 44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 180, 220)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var container = new StackPanel { Orientation = Orientation.Vertical };

            container.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 150, 200)),
                CornerRadius = new CornerRadius(18, 18, 0, 0),
                Padding = new Thickness(20, 14, 20, 14),
                Child = new TextBlock
                {
                    Text = "设备连接已断开",
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            });

            var body = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(24)
            };

            _reconnectStatusText = new TextBlock
            {
                Text = "正在重连中...",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(210, 220, 230)),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            };
            body.Children.Add(_reconnectStatusText);

            var progressHost = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(36, 48, 60)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 18),
                Child = new ProgressBar
                {
                    IsIndeterminate = true,
                    Height = 12,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 220))
                }
            };
            body.Children.Add(progressHost);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var button = new Button
            {
                Content = "取消重连",
                Width = 132,
                Height = 38,
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 180)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            button.Click += async (_, __) => await CancelReconnectAsync();
            buttonRow.Children.Add(button);
            body.Children.Add(buttonRow);

            container.Children.Add(body);
            panel.Child = container;
            overlay.Child = panel;
            return overlay;
        }

        private Border CreatePressureAlertOverlay()
        {
            var overlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(170, 40, 10, 10)),
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var panel = new Border
            {
                Width = 430,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromRgb(46, 20, 20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 120, 120)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var container = new StackPanel { Orientation = Orientation.Vertical };

            container.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(200, 70, 70)),
                CornerRadius = new CornerRadius(18, 18, 0, 0),
                Padding = new Thickness(20, 14, 20, 14),
                Child = new TextBlock
                {
                    Text = "压力超限",
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            });

            var body = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(24)
            };

            body.Children.Add(new TextBlock
            {
                Text = "检测到压力超限，已发送停止治疗指令。",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 225, 225)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            });

            var button = new Button
            {
                Content = "确认",
                Width = 120,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(180, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            button.Click += (_, __) => HidePressureAlertOverlay();
            body.Children.Add(button);

            container.Children.Add(body);
            panel.Child = container;
            overlay.Child = panel;
            return overlay;
        }

        private Border CreateTitleBar()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                BorderThickness = new Thickness(0, 0, 0, 2)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 标题
            var titleText = new TextBlock
            {
                Text = $"🏥 {AppConstants.AppName}",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 0, 0)
            };
            Grid.SetColumn(titleText, 0);
            grid.Children.Add(titleText);

            // 连接状态指示器
            var statusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0)
            };

            _wifiIndicator = CreateIndicator("WiFi");
            _serialIndicator = CreateIndicator("串口");

            statusPanel.Children.Add(_wifiIndicator);
            statusPanel.Children.Add(_serialIndicator);

            // 时间显示
            _timeText = new TextBlock
            {
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 15, 0)
            };

            Grid.SetColumn(statusPanel, 1);
            Grid.SetColumn(_timeText, 2);
            grid.Children.Add(statusPanel);
            grid.Children.Add(_timeText);

            border.Child = grid;
            return border;
        }

        private Border CreateIndicator(string label)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(5, 0, 5, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 70))
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.Red,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };

            var text = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            stack.Children.Add(dot);
            stack.Children.Add(text);
            border.Child = stack;

            return border;
        }

        private StackPanel CreateNavigationPanel()
        {
            var panel = new StackPanel
            {
                Background = new SolidColorBrush(Color.FromRgb(18, 28, 38))
            };

            var navItems = new[]
            {
            new { Name = "用户界面", Icon = "👤", ViewName = "UserInterface" },
            new { Name = "数据仪表盘", Icon = "📊", ViewName = "Dashboard" },
            new { Name = "用户管理", Icon = "🧑‍💼", ViewName = "UserManagement" },
            new { Name = "设备控制", Icon = "⚙️", ViewName = "Control" },
            new { Name = "历史记录", Icon = "📋", ViewName = "History" },
            new { Name = "系统设置", Icon = "🔧", ViewName = "Settings" }
        };

            foreach (var item in navItems)
            {
                var btn = CreateNavButton(item.Name, item.Icon);
                btn.Click += (s, e) => NavigateTo(item.ViewName);
                panel.Children.Add(btn);
            }

            return panel;
        }

        private Button CreateNavButton(string text, string icon)
        {
            var btn = new Button
            {
                Content = $"{icon}  {text}",
                Height = 50,
                Margin = new Thickness(5, 5, 5, 0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 210, 220)),
                BorderThickness = new Thickness(0),
                FontSize = 14,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(20, 0, 0, 0),
                Cursor = Cursors.Hand
            };

            // 悬停效果
            var style = new Style(typeof(Button));
            var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 150, 200))));
            trigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            style.Triggers.Add(trigger);
            btn.Style = style;

            return btn;
        }

        private Border CreateStatusBar()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusSummaryText = CreateStatusText("就绪", 1);
            _timeText = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 160, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 15, 0)
            };
            Grid.SetColumn(_timeText, 2);

            grid.Children.Add(_statusSummaryText);
            grid.Children.Add(_timeText);

            border.Child = grid;
            return border;
        }

        private TextBlock CreateStatusText(string text, int column)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 160, 180)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 0, 0)
            };
            Grid.SetColumn(tb, column);
            return tb;
        }

        private void NavigateTo(string viewName)
        {
            _ = NavigateToAsync(viewName);
        }

        private async Task NavigateToAsync(string viewName)
        {
            if (_contentFrame == null) return;

            try
            {
                // 清空导航历史，防止出现返回按钮
                while (_contentFrame.CanGoBack)
                {
                    _contentFrame.RemoveBackEntry();
                }

                switch (viewName)
                {
                    case "UserInterface":
                        var userInterfaceVm = _serviceProvider.GetRequiredService<UserInterfaceViewModel>();
                        await userInterfaceVm.RefreshUsersAsync();

                        if (_userInterfaceView == null)
                        {
                            _userInterfaceView = new UserInterfaceView(userInterfaceVm, _communicationService);
                        }

                        // 如果之前确认过用户，检查该用户是否仍在数据库中
                        // 不在则自动退出回到选择页面；在则保持当前状态
                        if (userInterfaceVm.IsConfirmed)
                        {
                            await userInterfaceVm.ValidateCurrentSelectionAsync();
                        }
                        _contentFrame.Content = _userInterfaceView;
                        break;

                    case "Dashboard":
                        if (_dashboardView == null)
                        {
                            var dashboardVM = _serviceProvider.GetRequiredService<DashboardViewModel>();
                            _dashboardView = new DashboardView(dashboardVM);
                        }
                        _contentFrame.Content = _dashboardView;
                        _viewModel.DashboardViewModel = _viewModel.DashboardViewModel ??
                            _serviceProvider.GetRequiredService<DashboardViewModel>();
                        break;

                    case "UserManagement":
                        if (_userManagementView == null)
                        {
                            var userManagementVm = _serviceProvider.GetRequiredService<UserManagementViewModel>();
                            _userManagementView = new UserManagementView(userManagementVm);
                        }
                        _contentFrame.Content = _userManagementView;
                        break;

                    case "Control":
                        if (_controlPanelView == null)
                        {
                            var controlVM = _serviceProvider.GetRequiredService<ControlPanelViewModel>();
                            _controlPanelView = new ControlPanelView(controlVM);
                        }
                        _contentFrame.Content = _controlPanelView;
                        _viewModel.ControlPanelViewModel = _viewModel.ControlPanelViewModel ??
                            _serviceProvider.GetRequiredService<ControlPanelViewModel>();

                        _ = Dispatcher.BeginInvoke(new Action(RefreshControlPanelForNavigation), System.Windows.Threading.DispatcherPriority.Loaded);
                        break;

                    case "History":
                        if (_historyView == null)
                        {
                            var historyVM = _serviceProvider.GetRequiredService<HistoryViewModel>();
                            _historyView = new HistoryView(historyVM);
                        }
                        _contentFrame.Content = _historyView;
                        _viewModel.HistoryViewModel = _viewModel.HistoryViewModel ??
                            _serviceProvider.GetRequiredService<HistoryViewModel>();
                        break;

                    case "Settings":
                        if (_settingsView == null)
                        {
                            _settingsViewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
                            _settingsView = new SettingsView(_settingsViewModel);
                        }
                        else if (_settingsViewModel == null)
                        {
                            _settingsViewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
                        }
                        _contentFrame.Content = _settingsView;
                        _viewModel.SettingsViewModel = _viewModel.SettingsViewModel ?? _settingsViewModel;

                        // 切换到设置页面时也刷新
                        _settingsViewModel?.RefreshConnectionStatus();
                        break;
                }

                // 强制刷新命令系统
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"导航到视图 {viewName} 失败: {ex}");
            }
        }

        private void BindViewModelEvents()
        {
            _viewModel.PropertyChanged += (s, e) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (e.PropertyName == nameof(MainViewModel.SystemStatus))
                    {
                        UpdateStatusIndicators();
                    }
                    if (e.PropertyName == nameof(MainViewModel.CurrentTime))
                    {
                        _timeText!.Text = _viewModel.CurrentTime;
                    }
                });
            };
        }

        private void OnCommunicationSystemStatusChanged(object? sender, SystemStatus status)
        {
            var previous = _viewModel.SystemStatus;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (status.WifiState == ConnectionState.Connected || status.SerialState == ConnectionState.Connected)
                    {
                        _reconnectSuppressed = false;
                        HideReconnectOverlay();
                    }

                    if (TryGetReconnectMode(previous, status, out var reconnectMode))
                    {
                        BeginReconnect(reconnectMode, status);
                    }

                    _viewModel.SystemStatus = status;
                    UpdateStatusIndicators();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OnCommunicationSystemStatusChanged] 异常: {ex}");
                }
            });
        }

        /// <summary>
        /// 底层监控检测到设备物理拔出时直接触发（不经过 Router 合并，比 SystemStatusChanged 更可靠）
        /// </summary>
        private void OnDevicePhysicallyDisconnected(object? sender, string reason)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_reconnectSuppressed || _isReconnectOverlayVisible)
                    {
                        return;
                    }

                    var mode = GetCurrentMode();
                    if (mode is CommunicationMode.None or CommunicationMode.WiFi)
                    {
                        // 当前不在串口模式，忽略
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"[OnDevicePhysicallyDisconnected] 触发重连弹窗: {reason}");
                    BeginReconnect(CommunicationMode.Serial, new SystemStatus
                    {
                        SerialState = ConnectionState.Disconnected,
                        LastLog = reason
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OnDevicePhysicallyDisconnected] 异常: {ex}");
                }
            });
        }

        /// <summary>
        /// WiFi 底层检测到物理断开或心跳超时时触发
        /// </summary>
        private void OnWifiPhysicallyDisconnected(object? sender, string reason)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_reconnectSuppressed || _isReconnectOverlayVisible)
                    {
                        return;
                    }

                    var mode = GetCurrentMode();
                    if (mode is CommunicationMode.None or CommunicationMode.Serial)
                    {
                        // 当前不在 WiFi 模式，忽略
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"[OnWifiPhysicallyDisconnected] 触发 WiFi 重连弹窗: {reason}");
                    BeginReconnect(CommunicationMode.WiFi, new SystemStatus
                    {
                        WifiState = ConnectionState.Disconnected,
                        LastLog = reason
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OnWifiPhysicallyDisconnected] 异常: {ex}");
                }
            });
        }

        private bool TryGetReconnectMode(SystemStatus previous, SystemStatus current, out CommunicationMode mode)
        {
            mode = CommunicationMode.None;

            if (_reconnectSuppressed)
            {
                return false;
            }

            if ((previous.WifiState == ConnectionState.Connected && current.WifiState is ConnectionState.Disconnected or ConnectionState.Error) ||
                (current.WifiState == ConnectionState.Connected && previous.WifiState is ConnectionState.Disconnected or ConnectionState.Error))
            {
                mode = CommunicationMode.WiFi;
                return true;
            }

            if ((previous.SerialState == ConnectionState.Connected && current.SerialState is ConnectionState.Disconnected or ConnectionState.Error) ||
                (current.SerialState == ConnectionState.Connected && previous.SerialState is ConnectionState.Disconnected or ConnectionState.Error))
            {
                mode = CommunicationMode.Serial;
                return true;
            }

            return false;
        }

        private CommunicationMode GetCurrentMode()
        {
            return _communicationService is CommunicationServiceRouter router ? router.CurrentMode : CommunicationMode.None;
        }

        private void BeginReconnect(CommunicationMode mode, SystemStatus status)
        {
            if (_isReconnectOverlayVisible)
            {
                return;
            }

            ShowReconnectOverlay(mode, status.LastLog);
            _reconnectCancellation?.Cancel();
            _reconnectCancellation?.Dispose();
            _reconnectCancellation = new CancellationTokenSource();
            _ = Task.Run(() => ReconnectLoopAsync(mode, _reconnectCancellation.Token));
        }

        private void ShowReconnectOverlay(CommunicationMode mode, string? reason)
        {
            if (_reconnectOverlay == null || _reconnectStatusText == null)
            {
                return;
            }

            _reconnectStatusText.Text = string.IsNullOrWhiteSpace(reason)
                ? $"{mode} 连接已断开，正在重连中..."
                : $"{reason}\n正在重连中...";
            _reconnectOverlay.Visibility = Visibility.Visible;
            _isReconnectOverlayVisible = true;
        }

        private void HideReconnectOverlay()
        {
            if (_reconnectOverlay == null)
            {
                return;
            }

            _reconnectOverlay.Visibility = Visibility.Collapsed;
            _isReconnectOverlayVisible = false;
        }

        private async Task ReconnectLoopAsync(CommunicationMode mode, CancellationToken cancellationToken)
        {
            var reconnectConfig = GetReconnectConfig();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var connected = mode switch
                    {
                        CommunicationMode.WiFi => await _communicationService!.ConnectAsync(reconnectConfig.WifiIpAddress, reconnectConfig.WifiPort),
                        CommunicationMode.Serial => await _communicationService!.ConnectSerialAsync(reconnectConfig.SerialPortName, reconnectConfig.BaudRate),
                        _ => false
                    };

                    if (connected)
                    {
                        Application.Current?.Dispatcher.Invoke(HideReconnectOverlay);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"重连失败：{ex}");
                }

                try
                {
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private ReconnectConfig GetReconnectConfig()
        {
            if (_serviceProvider.GetService<SettingsViewModel>() is SettingsViewModel settings)
            {
                return new ReconnectConfig
                {
                    WifiIpAddress = string.IsNullOrWhiteSpace(settings.WifiIpAddress) ? AppConstants.DefaultWifiIp : settings.WifiIpAddress,
                    WifiPort = settings.WifiPort > 0 ? settings.WifiPort : AppConstants.DefaultWifiPort,
                    SerialPortName = string.IsNullOrWhiteSpace(settings.SelectedComPort) ? "COM3" : settings.SelectedComPort,
                    BaudRate = settings.BaudRate > 0 ? settings.BaudRate : AppConstants.DefaultBaudRate
                };
            }

            return new ReconnectConfig();
        }

        private async Task CancelReconnectAsync()
        {
            _reconnectSuppressed = true;
            _reconnectCancellation?.Cancel();

            try
            {
                if (_communicationService is CommunicationServiceRouter router)
                {
                    if (router.CurrentMode == CommunicationMode.WiFi)
                    {
                        await _communicationService.DisconnectAsync();
                    }
                    else if (router.CurrentMode == CommunicationMode.Serial)
                    {
                        await _communicationService.DisconnectSerialAsync();
                    }
                }
                else if (_viewModel.SystemStatus.WifiState == ConnectionState.Connected)
                {
                    await _communicationService!.DisconnectAsync();
                }
                else if (_viewModel.SystemStatus.SerialState == ConnectionState.Connected)
                {
                    await _communicationService!.DisconnectSerialAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"取消重连时断开连接失败：{ex}");
            }
            finally
            {
                HideReconnectOverlay();
            }
        }

        private void OnPressureDataReceived(object? sender, PressureDataReport report)
        {
            var hasAlert = report.Items.Any(item => item.AlertFlag != 0 || item.CurrentValue > item.Threshold);
            if (!hasAlert)
            {
                return;
            }

            if (_isPressureAlertVisible)
            {
                return;
            }

            _isPressureAlertVisible = true;
            Application.Current?.Dispatcher.Invoke(ShowPressureAlertOverlay);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _communicationService!.SendCommandAsync(global::Upcomputer.Core.Models.ProtocolCommandCode.StopTherapy);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"发送停止治疗指令失败：{ex}");
                }

                try
                {
                    _serviceProvider.GetRequiredService<TreatmentEngine>().StopTreatment();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"停止本地治疗失败：{ex}");
                }
            });
        }

        private void ShowPressureAlertOverlay()
        {
            if (_pressureAlertOverlay != null)
            {
                _pressureAlertOverlay.Visibility = Visibility.Visible;
            }
        }

        private void HidePressureAlertOverlay()
        {
            if (_pressureAlertOverlay != null)
            {
                _pressureAlertOverlay.Visibility = Visibility.Collapsed;
            }

            _isPressureAlertVisible = false;
        }

        private void UpdateStatusIndicators()
        {
            var status = _viewModel.SystemStatus;
            UpdateIndicatorDot(_wifiIndicator, status.WifiState);
            UpdateIndicatorDot(_serialIndicator, status.SerialState);
            _statusSummaryText!.Text = BuildStatusSummary(status);
        }

        private void OnSerialConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        {
            _isSerialConnected = e.IsConnected;

            var current = _viewModel.SystemStatus;
            var updated = new SystemStatus
            {
                WifiState = current.WifiState,
                SerialState = e.IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected,
                Latency = current.Latency,
                TcpLatency = current.TcpLatency,
                UdpLatency = current.UdpLatency,
                SerialLatency = current.SerialLatency,
                IsSerialTransport = current.IsSerialTransport,
                PacketLoss = current.PacketLoss,
                IsDeviceConnected = current.IsDeviceConnected,
                UdpPacketsReceived = current.UdpPacketsReceived,
                UdpPacketsDropped = current.UdpPacketsDropped,
                LastLog = string.IsNullOrWhiteSpace(e.Message) ? current.LastLog : e.Message
            };

            Application.Current?.Dispatcher.Invoke(() =>
            {
                _viewModel.SystemStatus = updated;
                UpdateStatusIndicators();

                RefreshControlPanelSerialState();
            });
        }

        private async Task SwitchToSerialIfNeededAsync()
        {
            if (_settingsViewModel == null)
            {
                return;
            }

            if (_communicationService is CommunicationServiceRouter)
            {
                return;
            }

            _communicationService = _serviceProvider.GetRequiredService<ICommunicationService>();
            await Task.CompletedTask;
        }

        private static string BuildStatusSummary(SystemStatus status)
        {
            var wifiText = status.WifiState switch
            {
                ConnectionState.Connected => "WiFi 已连接",
                ConnectionState.Connecting => "WiFi 连接中",
                ConnectionState.Error => "WiFi 错误",
                _ => "WiFi 未连接"
            };

            var deviceText = status.IsDeviceConnected ? "设备已连接" : "设备未连接";
            var metricsText = status.IsDeviceConnected
                ? $"{status.TcpLatencyDisplay} | {status.UdpLatencyDisplay} | {status.SerialLatencyDisplay} | 丢包 {status.PacketLoss:F2}%"
                : $"{status.TcpLatencyDisplay} | {status.UdpLatencyDisplay} | {status.SerialLatencyDisplay} | 等待设备数据";

            var parts = new List<string> { wifiText, deviceText, metricsText };

            if (status.SerialState != ConnectionState.Disconnected)
            {
                parts.Insert(1, status.SerialState switch
                {
                    ConnectionState.Connected => "串口已连接",
                    ConnectionState.Connecting => "串口连接中",
                    ConnectionState.Error => "串口错误",
                    _ => "串口未连接"
                });
            }

            return string.Join(" | ", parts);
        }

        private void RefreshControlPanelForNavigation()
        {
            var controlPanelViewModel = _viewModel.ControlPanelViewModel;
            if (controlPanelViewModel == null)
            {
                return;
            }

            controlPanelViewModel.SyncConnectionState();
            controlPanelViewModel.RefreshAllCommands();
            CommandManager.InvalidateRequerySuggested();
        }

        private void RefreshControlPanelSerialState()
        {
            var controlPanelViewModel = _viewModel.ControlPanelViewModel;
            if (controlPanelViewModel == null)
            {
                return;
            }

            controlPanelViewModel.SetSerialConnectionState(_isSerialConnected);
            controlPanelViewModel.RefreshAllCommands();
        }

        private void UpdateIndicatorDot(Border? indicator, ConnectionState state)
        {
            if (indicator == null)
            {
                return;
            }

            if (indicator.Child is not StackPanel stack)
            {
                return;
            }

            var dot = stack.Children.OfType<System.Windows.Shapes.Ellipse>().FirstOrDefault();
            if (dot != null)
            {
                dot.Fill = state switch
                {
                    ConnectionState.Connected => new SolidColorBrush(Color.FromRgb(0, 255, 0)),
                    ConnectionState.Connecting => new SolidColorBrush(Color.FromRgb(255, 255, 0)),
                    ConnectionState.Error => new SolidColorBrush(Color.FromRgb(255, 0, 0)),
                    _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))
                };
            }

            var textBlock = stack.Children.OfType<TextBlock>().FirstOrDefault();
            if (textBlock == null)
            {
                return;
            }

            textBlock.Text = textBlock.Text.StartsWith("串口")
                ? state switch
                {
                    ConnectionState.Connected => "串口 已连接",
                    ConnectionState.Connecting => "串口 连接中...",
                    ConnectionState.Error => "串口 错误",
                    _ => "串口 未连接"
                }
                : state switch
                {
                    ConnectionState.Connected => "WiFi 已连接",
                    ConnectionState.Connecting => "WiFi 连接中...",
                    ConnectionState.Error => "WiFi 错误",
                    _ => "WiFi 未连接"
                };
        }

        private sealed class ReconnectConfig
        {
            public string WifiIpAddress { get; set; } = AppConstants.DefaultWifiIp;
            public int WifiPort { get; set; } = AppConstants.DefaultWifiPort;
            public string SerialPortName { get; set; } = "COM3";
            public int BaudRate { get; set; } = AppConstants.DefaultBaudRate;
        }
    }
}
