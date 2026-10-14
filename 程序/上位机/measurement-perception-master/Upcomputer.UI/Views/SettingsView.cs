using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Upcomputer.UI.ViewModels;

namespace Upcomputer.UI.Views
{
    public class SettingsView : UserControl
    {
        private readonly SettingsViewModel _viewModel;

        public SettingsView(SettingsViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;
            BuildUserInterface();
        }

        private void BuildUserInterface()
        {
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(15)
            };

            var mainStack = new StackPanel();

            mainStack.Children.Add(CreateWifiSettingsPanel());
            mainStack.Children.Add(CreateSerialSettingsPanel());
            mainStack.Children.Add(CreateThresholdSettingsPanel());
            mainStack.Children.Add(CreateCloudSettingsPanel());
            mainStack.Children.Add(CreateLogPanel());

            scrollViewer.Content = mainStack;
            Content = scrollViewer;
        }

        private Border CreateWifiSettingsPanel()
        {
            var border = CreateSectionBorder("📶 WiFi 设置");
            var stack = new StackPanel();

            // IP 地址输入行
            var ipPanel = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            ipPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ipPanel.ColumnDefinitions.Add(new ColumnDefinition());

            var ipLabel = new TextBlock { Text = "IP 地址:", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(ipLabel, 0);
            ipPanel.Children.Add(ipLabel);

            var ipBox = new TextBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            ipBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("WifiIpAddress"));
            Grid.SetColumn(ipBox, 1);
            ipPanel.Children.Add(ipBox);

            stack.Children.Add(ipPanel);

            // 端口输入行
            var portPanel = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            portPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            portPanel.ColumnDefinitions.Add(new ColumnDefinition());

            var portLabel = new TextBlock { Text = "端口:", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(portLabel, 0);
            portPanel.Children.Add(portLabel);

            var portBox = new TextBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            portBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("WifiPort"));
            Grid.SetColumn(portBox, 1);
            portPanel.Children.Add(portBox);

            stack.Children.Add(portPanel);

            // 扫描设备入口
            var scanPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };

            var scanBtn = new Button
            {
                Content = "🔍 扫描设备",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            scanBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ScanDevicesCommand"));
            scanPanel.Children.Add(scanBtn);

            // 扫描状态提示
            var scanStatusText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 170, 190)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(15, 0, 0, 0)
            };
            scanStatusText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("ScanStatus"));
            scanPanel.Children.Add(scanStatusText);

            stack.Children.Add(scanPanel);

            var provisionBtn = new Button
            {
                Content = "📶 设备配网",
                Width = 120,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(155, 89, 182)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 15)
            };
            provisionBtn.Click += (_, _) => _viewModel.OpenDeviceProvisioningGuide();
            stack.Children.Add(provisionBtn);
            // 设备列表下拉框（扫描结果显示）
            var deviceCombo = new ComboBox
            {
                Height = 30,
                Margin = new Thickness(0, 0, 0, 15),
                DisplayMemberPath = "DisplayText"
            };
            deviceCombo.SetBinding(ComboBox.ItemsSourceProperty, new System.Windows.Data.Binding("DiscoveredDevices"));
            deviceCombo.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding("SelectedDevice"));
            stack.Children.Add(deviceCombo);

            // 连接/断开按钮
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var connectBtn = new Button
            {
                Content = "🔗 连接",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0)
            };
            connectBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ConnectWifiCommand"));
            buttonPanel.Children.Add(connectBtn);

            var disconnectBtn = new Button
            {
                Content = "❌ 断开",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(200, 60, 50)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            disconnectBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("DisconnectWifiCommand"));
            buttonPanel.Children.Add(disconnectBtn);

            stack.Children.Add(buttonPanel);

            border.Child = stack;
            return border;
        }

        private Border CreateSerialSettingsPanel()
        {
            var border = CreateSectionBorder("🔌 串口设置");

            var stack = new StackPanel();

            var comPanel = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            comPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            comPanel.ColumnDefinitions.Add(new ColumnDefinition());

            var comLabel = new TextBlock { Text = "端口:", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(comLabel, 0);
            comPanel.Children.Add(comLabel);

            var comCombo = new ComboBox { Height = 30 };
            comCombo.SetBinding(ComboBox.ItemsSourceProperty, new System.Windows.Data.Binding("AvailableComPorts"));
            comCombo.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding("SelectedComPort"));
            Grid.SetColumn(comCombo, 1);
            comPanel.Children.Add(comCombo);

            stack.Children.Add(comPanel);

            var baudPanel = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            baudPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            baudPanel.ColumnDefinitions.Add(new ColumnDefinition());

            var baudLabel = new TextBlock { Text = "波特率:", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(baudLabel, 0);
            baudPanel.Children.Add(baudLabel);

            var baudCombo = new ComboBox { Height = 30 };
            baudCombo.SetBinding(ComboBox.ItemsSourceProperty, new System.Windows.Data.Binding("BaudRates"));
            baudCombo.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding("BaudRate"));
            Grid.SetColumn(baudCombo, 1);
            baudPanel.Children.Add(baudCombo);

            stack.Children.Add(baudPanel);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

            var refreshBtn = new Button
            {
                Content = "🔄 刷新串口",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0)
            };
            refreshBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("RefreshComPortsCommand"));
            buttonPanel.Children.Add(refreshBtn);

            var connectBtn = new Button
            {
                Content = "🔗 打开",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0)
            };
            connectBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ConnectSerialCommand"));
            buttonPanel.Children.Add(connectBtn);

            var disconnectBtn = new Button
            {
                Content = "❌ 关闭",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(200, 60, 50)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            disconnectBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("DisconnectSerialCommand"));
            buttonPanel.Children.Add(disconnectBtn);

            stack.Children.Add(buttonPanel);

            border.Child = stack;
            return border;
        }

        private Border CreateThresholdSettingsPanel()
        {
            var border = CreateSectionBorder("⚠️ 告警阈值");

            var stack = new StackPanel();

            var pressurePanel = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            pressurePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pressurePanel.ColumnDefinitions.Add(new ColumnDefinition());
            pressurePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pressureLabel = new TextBlock { Text = "压力上限:", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(pressureLabel, 0);
            pressurePanel.Children.Add(pressureLabel);

            var pressureSlider = new Slider { Minimum = 0, Maximum = 150, TickFrequency = 10, Height = 30 };
            pressureSlider.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding("PressureThreshold"));
            Grid.SetColumn(pressureSlider, 1);
            pressurePanel.Children.Add(pressureSlider);

            var pressureValue = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)), Margin = new Thickness(10, 0, 0, 0) };
            pressureValue.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("PressureThreshold") { StringFormat = "{0:F0} g" });
            Grid.SetColumn(pressureValue, 2);
            pressurePanel.Children.Add(pressureValue);

            stack.Children.Add(pressurePanel);

            var edemaPanel = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            edemaPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            edemaPanel.ColumnDefinitions.Add(new ColumnDefinition());
            edemaPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var edemaLabel = new TextBlock { Text = "水肿上限:", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(edemaLabel, 0);
            edemaPanel.Children.Add(edemaLabel);

            var edemaSlider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, Height = 30 };
            edemaSlider.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding("EdemaThreshold"));
            Grid.SetColumn(edemaSlider, 1);
            edemaPanel.Children.Add(edemaSlider);

            var edemaValue = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)), Margin = new Thickness(10, 0, 0, 0) };
            edemaValue.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("EdemaThreshold") { StringFormat = "{0:F0}%" });
            Grid.SetColumn(edemaValue, 2);
            edemaPanel.Children.Add(edemaValue);

            stack.Children.Add(edemaPanel);

            var saveBtn = new Button
            {
                Content = "💾 保存阈值",
                Width = 120,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            saveBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("SaveThresholdsCommand"));
            stack.Children.Add(saveBtn);

            border.Child = stack;
            return border;
        }

        private Border CreateCloudSettingsPanel()
        {
            var border = CreateSectionBorder("☁️ 云服务配置");

            var stack = new StackPanel();

            var syncCheck = new CheckBox
            {
                Content = "启用云端数据同步",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 210, 220)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            syncCheck.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("CloudSyncEnabled"));
            stack.Children.Add(syncCheck);

            var endpointPanel = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            endpointPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            endpointPanel.ColumnDefinitions.Add(new ColumnDefinition());

            var endpointLabel = new TextBlock { Text = "服务器地址:", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(endpointLabel, 0);
            endpointPanel.Children.Add(endpointLabel);

            var endpointBox = new TextBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            endpointBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("CloudEndpoint"));
            Grid.SetColumn(endpointBox, 1);
            endpointPanel.Children.Add(endpointBox);

            stack.Children.Add(endpointPanel);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var saveBtn = new Button
            {
                Content = "💾 保存配置",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0)
            };
            saveBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("SaveCloudSettingsCommand"));
            buttonPanel.Children.Add(saveBtn);

            var testBtn = new Button
            {
                Content = "📡 测试时延",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(52, 73, 94)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            testBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("TestLatencyCommand"));
            buttonPanel.Children.Add(testBtn);

            stack.Children.Add(buttonPanel);

            border.Child = stack;
            return border;
        }

        private Border CreateLogPanel()
        {
            var border = CreateSectionBorder("📜 连接日志");

            var logBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 150,
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                BorderThickness = new Thickness(0)
            };
            logBox.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("ConnectionLog"));

            border.Child = logBox;
            return border;
        }

        private Border CreateSectionBorder(string title)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(25, 35, 45)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 15),
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                Margin = new Thickness(12, 10, 12, 8)
            };
            Grid.SetRow(titleBlock, 0);
            grid.Children.Add(titleBlock);

            var contentPresenter = new ContentPresenter { Margin = new Thickness(12, 0, 12, 12) };
            Grid.SetRow(contentPresenter, 1);
            grid.Children.Add(contentPresenter);

            border.Child = grid;
            return border;
        }

    }
}
