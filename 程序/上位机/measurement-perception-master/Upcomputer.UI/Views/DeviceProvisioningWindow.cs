using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Upcomputer.UI.Views
{
    public class DeviceProvisioningWindow : Window
    {
        private const string TargetHotspotSsid = "RehabDevice_Config";
        private const string DeviceConfigUrl = "http://192.168.4.1/set";

        private readonly ObservableCollection<string> _availableNetworks = new();
        private readonly ObservableCollection<string> _deviceWifiNetworks = new();
        private ListBox _wifiListBox = null!;
        private ListBox _deviceWifiListBox = null!;
        private readonly TextBlock _statusText;
        private readonly TextBlock _instructionText;
        private readonly TextBlock _detailText;
        private Button _nextButton = null!;
        private readonly Grid _step1Panel;
        private readonly Grid _step2Panel;
        private TextBox _devicePasswordBox = null!;
        private Button _submitButton = null!;

        private string? _currentConnectedSsid;
        private bool _isTargetConnected;

        public DeviceProvisioningWindow()
        {
            Title = "设备配网";
            Width = 460;
            Height = 760;
            MinWidth = 420;
            MinHeight = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(15, 25, 35));
            Foreground = Brushes.White;
            ShowInTaskbar = true;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = "设备配网引导",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255))
            });
            titleStack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Text = "请先连接设备热点，再为设备选择目标 WiFi。",
                Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 220)),
                TextWrapping = TextWrapping.Wrap
            });
            titleBorder.Child = titleStack;
            Grid.SetRow(titleBorder, 0);
            root.Children.Add(titleBorder);

            var contentGrid = new Grid { Margin = new Thickness(16) };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _statusText = new TextBlock
            {
                Text = "正在扫描当前 WiFi...",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 80)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_statusText, 0);
            contentGrid.Children.Add(_statusText);

            _instructionText = new TextBlock
            {
                Text = "1. 找到并连接热点 RehabDevice_Config。\n2. 连接成功后点击“下一步”。\n3. 选择设备要连接的网络并提交。",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 210, 220)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_instructionText, 1);
            contentGrid.Children.Add(_instructionText);

            var stepHost = new Grid();
            Grid.SetRow(stepHost, 2);
            contentGrid.Children.Add(stepHost);

            _step1Panel = CreateStep1Panel();
            _step2Panel = CreateStep2Panel();
            stepHost.Children.Add(_step1Panel);
            stepHost.Children.Add(_step2Panel);
            _step2Panel.Visibility = Visibility.Collapsed;

            Grid.SetRow(contentGrid, 1);
            root.Children.Add(contentGrid);

            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _detailText = new TextBlock
            {
                Text = "如果未发现 RehabDevice_Config，请检查设备是否通电或是否已经联网。",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 190, 200)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(_detailText, 0);
            footerGrid.Children.Add(_detailText);

            _nextButton = new Button
            {
                Content = "下一步",
                Width = 110,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center
            };
            _nextButton.Click += (_, _) => HandlePrimaryAction();
            Grid.SetColumn(_nextButton, 1);
            footerGrid.Children.Add(_nextButton);

            var refreshButton = new Button
            {
                Content = "刷新",
                Width = 80,
                Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            refreshButton.Click += async (_, _) => await RefreshNetworksAsync();
            Grid.SetColumn(refreshButton, 2);
            footerGrid.Children.Add(refreshButton);

            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
            Loaded += async (_, _) =>
            {
                await RefreshNetworksAsync();
            };
        }

        private Grid CreateStep1Panel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new TextBlock
            {
                Text = "步骤 1：连接设备热点",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(header, 0);
            panel.Children.Add(header);

            _wifiListBox = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(25, 35, 45)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1),
                MinHeight = 420,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            _wifiListBox.ItemsSource = _availableNetworks;
            _wifiListBox.SelectionChanged += (_, _) => UpdateSelectedNetworkState();
            Grid.SetRow(_wifiListBox, 1);
            panel.Children.Add(_wifiListBox);

            return panel;
        }

        private Grid CreateStep2Panel()
        {
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            panel.Children.Add(new TextBlock
            {
                Text = "步骤 2：选择设备要连接的网络",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "请选择家里或办公室的 WiFi，并输入密码后保存到设备。",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 210, 220)),
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetRow((UIElement)panel.Children[1], 1);

            _deviceWifiListBox = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(25, 35, 45)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1),
                MinHeight = 220,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 12),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _deviceWifiListBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            _deviceWifiListBox.ItemsSource = _deviceWifiNetworks;
            _deviceWifiListBox.SelectionChanged += (_, _) => UpdateSubmitState();
            Grid.SetRow(_deviceWifiListBox, 2);
            panel.Children.Add(_deviceWifiListBox);

            var form = new StackPanel();
            form.Children.Add(new TextBlock
            {
                Text = "WiFi 密码",
                Foreground = new SolidColorBrush(Color.FromRgb(200, 210, 220)),
                Margin = new Thickness(0, 0, 0, 6)
            });

            _devicePasswordBox = new TextBox
            {
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(35, 45, 55)),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 90, 110)),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            _devicePasswordBox.TextChanged += (_, _) => UpdateSubmitState();
            form.Children.Add(_devicePasswordBox);

            _submitButton = new Button
            {
                Content = "连接到WiFi",
                Width = 120,
                Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(155, 89, 182)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = false
            };
            _submitButton.Click += async (_, _) => await SubmitConfigAsync();
            form.Children.Add(_submitButton);

            Grid.SetRow(form, 3);
            panel.Children.Add(form);

            return panel;
        }

        private async Task RefreshNetworksAsync()
        {
            _statusText.Text = "正在扫描当前 WiFi...";
            _detailText.Text = "如果未发现 RehabDevice_Config，请检查设备是否通电或是否已经联网。";
            _nextButton.Visibility = Visibility.Collapsed;
            _currentConnectedSsid = null;
            _isTargetConnected = false;

            var scanResult = await Task.Run(() => GetWifiNetworks());
            _availableNetworks.Clear();
            foreach (var network in scanResult)
            {
                _availableNetworks.Add(network);
            }

            _deviceWifiNetworks.Clear();
            foreach (var network in scanResult.Where(s => !string.Equals(s, TargetHotspotSsid, StringComparison.OrdinalIgnoreCase)))
            {
                _deviceWifiNetworks.Add(network);
            }

            UpdateConnectionState(scanResult);
            UpdateSelectedNetworkState();

            if (_availableNetworks.Count == 0)
            {
                _statusText.Text = "未扫描到任何 WiFi。请确认无线网卡已开启。";
                return;
            }

            if (!_availableNetworks.Any(s => string.Equals(s, TargetHotspotSsid, StringComparison.OrdinalIgnoreCase)))
            {
                _statusText.Text = $"未发现 {TargetHotspotSsid}。可能是设备未通电，或者设备已连接到其它网络。";
                return;
            }

            if (_isTargetConnected)
            {
                _statusText.Text = $"已连接到 {TargetHotspotSsid}，可以进入下一步。";
                _nextButton.Visibility = Visibility.Visible;
                _detailText.Text = $"已检测到 {TargetHotspotSsid}，请点击下一步继续。";
            }
            else
            {
                _statusText.Text = $"已发现 {TargetHotspotSsid}，请在 Windows WiFi 列表中连接后再继续。";
                _detailText.Text = $"已发现 {TargetHotspotSsid}，当前尚未连接。连接后会自动显示“下一步”。";
            }
        }

        private void UpdateConnectionState(IEnumerable<string> scanResult)
        {
            _currentConnectedSsid = GetCurrentConnectedSsid();

            _isTargetConnected = string.Equals(_currentConnectedSsid, TargetHotspotSsid, StringComparison.OrdinalIgnoreCase);
            UpdatePrimaryActionState();
        }

        private void UpdateSelectedNetworkState()
        {
            if (_wifiListBox.SelectedItem is string selected)
            {
                if (string.Equals(selected, TargetHotspotSsid, StringComparison.OrdinalIgnoreCase))
                {
                    _detailText.Text = $"已选中 {TargetHotspotSsid}。连接后即可点击下一步。";
                }
                else
                {
                    _detailText.Text = $"当前选中：{selected}。请切换到 {TargetHotspotSsid} 以继续配网。";
                }

                UpdatePrimaryActionState();
            }
            else
            {
                _nextButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdatePrimaryActionState()
        {
            if (_wifiListBox.SelectedItem is string selected &&
                string.Equals(selected, TargetHotspotSsid, StringComparison.OrdinalIgnoreCase))
            {
                _nextButton.Visibility = Visibility.Visible;
                _nextButton.Content = _isTargetConnected ? "下一步" : "连接到WiFi";
                return;
            }

            _nextButton.Visibility = Visibility.Collapsed;
        }

        private void HandlePrimaryAction()
        {
            if (_isTargetConnected)
            {
                ShowStep2();
                return;
            }

            if (_wifiListBox.SelectedItem is string selected &&
                string.Equals(selected, TargetHotspotSsid, StringComparison.OrdinalIgnoreCase))
            {
                OpenWifiSettings();
            }
        }

        private static void OpenWifiSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:network-wifi",
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show("请手动打开系统 WiFi 设置并连接 RehabDevice_Config。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ShowStep2()
        {
            _step1Panel.Visibility = Visibility.Collapsed;
            _step2Panel.Visibility = Visibility.Visible;
            _instructionText.Text = "请选择设备要连接的目标 WiFi，并输入密码。";
            _statusText.Text = "步骤 2：配置设备联网信息";
        }

        private void UpdateSubmitState()
        {
            var hasSelection = _deviceWifiListBox.SelectedItem is string;
            _submitButton.Content = hasSelection ? "连接到WiFi" : "请选择WiFi";
            _submitButton.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(_devicePasswordBox.Text);
        }

        private async Task SubmitConfigAsync()
        {
            if (_deviceWifiListBox.SelectedItem is not string ssid)
            {
                MessageBox.Show(this, "请选择要连接的 WiFi。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var password = _devicePasswordBox.Text ?? string.Empty;

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("ssid", ssid),
                    new KeyValuePair<string, string>("pass", password)
                });

                var response = await client.PostAsync(DeviceConfigUrl, content);
                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show(this, "配置已发送到设备，设备正在保存并重启。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                }
                else
                {
                    MessageBox.Show(this, $"设备返回错误：{response.StatusCode}", "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"发送配置失败：{ex.Message}", "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static List<string> GetWifiNetworks()
        {
            var output = RunNetshCommand("wlan show networks mode=bssid");
            var ssids = new List<string>();

            foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(line, @"^\s*SSID\s+\d+\s*:\s*(.*)$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var ssid = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(ssid) && !ssids.Contains(ssid, StringComparer.OrdinalIgnoreCase))
                    {
                        ssids.Add(ssid);
                    }
                }
            }

            return ssids;
        }

        private static string? GetCurrentConnectedSsid()
        {
            var output = RunNetshCommand("wlan show interfaces");
            foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(line, @"^\s*(?:SSID|SSID 名称)\s*:\s*(.*)$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var ssid = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(ssid) && !ssid.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        return ssid;
                    }
                }
            }

            return null;
        }

        private static string RunNetshCommand(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return string.Empty;
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);
                return string.IsNullOrWhiteSpace(output) ? error : output;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
