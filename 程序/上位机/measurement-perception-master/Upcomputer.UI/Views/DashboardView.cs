using Syncfusion.UI.Xaml.Charts;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Upcomputer.Common.Enums;
using Upcomputer.UI.Controls;
using Upcomputer.UI.ViewModels;

namespace Upcomputer.UI.Views
{
    public class DashboardView : UserControl
    {
        private readonly DashboardViewModel _viewModel;

        public DashboardView(DashboardViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;

            BuildUserInterface();

            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            this.Unloaded -= OnUnloaded;
        }

        private void BuildUserInterface()
        {
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 左上：6路压力仪表盘
            var pressurePanel = CreatePressurePanel();
            Grid.SetRow(pressurePanel, 0);
            Grid.SetColumn(pressurePanel, 0);
            mainGrid.Children.Add(pressurePanel);

            // 右上：水肿数据 + 治疗进度
            var rightTopPanel = CreateRightTopPanel();
            Grid.SetRow(rightTopPanel, 0);
            Grid.SetColumn(rightTopPanel, 1);
            mainGrid.Children.Add(rightTopPanel);

            // 左下：水肿趋势曲线
            var curvePanel = CreateCurvePanel();
            Grid.SetRow(curvePanel, 1);
            Grid.SetColumn(curvePanel, 0);
            mainGrid.Children.Add(curvePanel);

            // 右下：设备状态总览
            var devicePanel = CreateDeviceStatusPanel();
            Grid.SetRow(devicePanel, 1);
            Grid.SetColumn(devicePanel, 1);
            mainGrid.Children.Add(devicePanel);

            Content = mainGrid;
        }

        private Border CreatePressurePanel()
        {
            var border = CreateSectionBorder("📊 实时压力监测");

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            for (int i = 0; i < 6; i++)
            {
                var gauge = new PressureGauge
                {
                    Title = $"压力{i + 1}",
                    Unit = "g",
                    Margin = new Thickness(5)
                };

                var binding = new System.Windows.Data.Binding($"PressureDataList[{i}].CurrentValue");
                gauge.SetBinding(PressureGauge.ValueProperty, binding);

                var thresholdBinding = new System.Windows.Data.Binding($"PressureDataList[{i}].Threshold");
                gauge.SetBinding(PressureGauge.ThresholdProperty, thresholdBinding);

                var maxBinding = new System.Windows.Data.Binding($"PressureDataList[{i}].MaxValue");
                gauge.SetBinding(PressureGauge.MaxValueProperty, maxBinding);

                Grid.SetRow(gauge, i / 3);
                Grid.SetColumn(gauge, i % 3);
                grid.Children.Add(gauge);
            }

            border.Child = grid;
            return border;
        }

        private Border CreateRightTopPanel()
        {
            var border = CreateSectionBorder("💧 水肿监测 & 治疗进度");

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 水肿数据
            var edemaPanel = new StackPanel { Margin = new Thickness(10) };

            for (int i = 0; i < _viewModel.EdemaDataList.Count; i++)
            {
                var edemaCard = CreateEdemaCard(i);
                edemaPanel.Children.Add(edemaCard);
            }

            Grid.SetRow(edemaPanel, 0);
            grid.Children.Add(edemaPanel);

            // 治疗进度
            var progressPanel = CreateProgressPanel();
            Grid.SetRow(progressPanel, 1);
            grid.Children.Add(progressPanel);

            border.Child = grid;
            return border;
        }

        private Border CreateEdemaCard(int index)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var titleBlock = new TextBlock
            {
                Text = $"水肿传感器 {index + 1}",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetColumnSpan(titleBlock, 2);

            var impedanceText = new TextBlock { FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            impedanceText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding($"EdemaDataList[{index}].DisplayImpedance"));

            var percentageText = new TextBlock { FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Brushes.Orange, HorizontalAlignment = HorizontalAlignment.Right };
            percentageText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding($"EdemaDataList[{index}].DisplayPercentage"));

            var stack = new StackPanel();
            stack.Children.Add(titleBlock);

            var valueGrid = new Grid();
            valueGrid.ColumnDefinitions.Add(new ColumnDefinition());
            valueGrid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(impedanceText, 0);
            Grid.SetColumn(percentageText, 1);
            valueGrid.Children.Add(impedanceText);
            valueGrid.Children.Add(percentageText);
            stack.Children.Add(valueGrid);

            border.Child = stack;
            return border;
        }

        private Border CreateProgressPanel()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(10),
                Padding = new Thickness(15)
            };

            var stack = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = "⏱️ 治疗进度",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            stack.Children.Add(titleBlock);

            // 模式显示
            var modeText = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220)) };
            modeText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("CurrentSession.ModeDisplay"));
            stack.Children.Add(modeText);

            // 时间显示
            var timePanel = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            timePanel.ColumnDefinitions.Add(new ColumnDefinition());
            timePanel.ColumnDefinitions.Add(new ColumnDefinition());

            var elapsedText = new TextBlock { FontSize = 16, Foreground = Brushes.White };
            elapsedText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("CurrentSession.ElapsedTimeDisplay"));

            var remainingText = new TextBlock { FontSize = 16, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)), HorizontalAlignment = HorizontalAlignment.Right };
            remainingText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("CurrentSession.RemainingTimeDisplay"));

            Grid.SetColumn(elapsedText, 0);
            Grid.SetColumn(remainingText, 1);
            timePanel.Children.Add(elapsedText);
            timePanel.Children.Add(remainingText);
            stack.Children.Add(timePanel);

            // 进度条
            var progressBar = new ProgressBar
            {
                Height = 8,
                Margin = new Thickness(0, 5, 0, 5)
            };
            progressBar.SetBinding(ProgressBar.ValueProperty, new System.Windows.Data.Binding("CurrentSession.Progress"));
            stack.Children.Add(progressBar);

            // 进度百分比
            var progressText = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 160, 180)), HorizontalAlignment = HorizontalAlignment.Right };
            progressText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("CurrentSession.Progress") { StringFormat = "{0:F0}%" });
            stack.Children.Add(progressText);

            border.Child = stack;
            return border;
        }

        private Border CreateCurvePanel()
        {
            var border = CreateSectionBorder("📈 数据趋势曲线");

            var panel = new StackPanel();

            var selectorPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10, 10, 10, 0)
            };

            selectorPanel.Children.Add(new TextBlock
            {
                Text = "曲线类型",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220))
            });

            var chartModeCombo = new ComboBox
            {
                Width = 140,
                Margin = new Thickness(10, 0, 0, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45)),
                Foreground = Brushes.Black
            };
            chartModeCombo.ItemContainerStyle = new Style(typeof(ComboBoxItem))
            {
                Setters =
                {
                    new Setter(ComboBoxItem.ForegroundProperty, Brushes.Black)
                }
            };
            chartModeCombo.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("ChartModes"));
            chartModeCombo.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding("SelectedChartMode") { Mode = BindingMode.TwoWay });
            selectorPanel.Children.Add(chartModeCombo);

            panel.Children.Add(selectorPanel);

            var chartHost = new Grid { Margin = new Thickness(10) };

            var pressureChart = CreatePressureChart();
            pressureChart.SetBinding(VisibilityProperty, new Binding("SelectedChartMode")
            {
                Converter = new ChartModeToVisibilityConverter(),
                ConverterParameter = "压力数据"
            });
            chartHost.Children.Add(pressureChart);

            var edemaChart = CreateEdemaChart();
            edemaChart.SetBinding(VisibilityProperty, new Binding("SelectedChartMode")
            {
                Converter = new ChartModeToVisibilityConverter(),
                ConverterParameter = "水肿阻抗"
            });
            chartHost.Children.Add(edemaChart);

            panel.Children.Add(chartHost);
            border.Child = panel;
            return border;
        }

        private SfChart CreateEdemaChart()
        {
            var sfChart = new SfChart
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45))
            };

            sfChart.PrimaryAxis = new CategoryAxis
            {
                Header = "采样次数",
                HeaderStyle = new LabelStyle { FontSize = 12, Foreground = Brushes.White },
                LabelStyle = new LabelStyle { FontSize = 10, Foreground = Brushes.White },
                ShowGridLines = false
            };

            sfChart.SecondaryAxis = new NumericalAxis
            {
                Header = "阻抗 (Ω)",
                HeaderStyle = new LabelStyle { FontSize = 12, Foreground = Brushes.White },
                LabelStyle = new LabelStyle { FontSize = 10, Foreground = Brushes.White },
                ShowGridLines = true
            };

            var lineSeries = new LineSeries
            {
                XBindingPath = "SampleDisplay",
                YBindingPath = "Value",
                ItemsSource = _viewModel.EdemaHistory,
                Interior = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                StrokeThickness = 2,
                Foreground = Brushes.White,
                Label = "当前阻抗"
            };
            lineSeries.AdornmentsInfo = new ChartAdornmentInfo { ShowMarker = true, Symbol = ChartSymbol.Ellipse };
            sfChart.Series.Add(lineSeries);

            sfChart.Legend = new ChartLegend
            {
                DockPosition = ChartDock.Top,
                ItemMargin = new Thickness(5),
                CheckBoxVisibility = Visibility.Collapsed,
                Foreground = Brushes.White
            };

            return sfChart;
        }

        private SfChart CreatePressureChart()
        {
            var sfChart = new SfChart
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45))
            };

            sfChart.PrimaryAxis = new CategoryAxis
            {
                Header = "时间",
                HeaderStyle = new LabelStyle { FontSize = 12, Foreground = Brushes.White },
                LabelStyle = new LabelStyle { FontSize = 10, Foreground = Brushes.White },
                ShowGridLines = false
            };

            sfChart.SecondaryAxis = new NumericalAxis
            {
                Header = "压力值 (g)",
                HeaderStyle = new LabelStyle { FontSize = 12, Foreground = Brushes.White },
                LabelStyle = new LabelStyle { FontSize = 10, Foreground = Brushes.White },
                ShowGridLines = true
            };

            AddPressureSeries(sfChart, "Sensor1", "1号传感器", _viewModel.PressureChartHistory, System.Windows.Media.Color.FromRgb(52, 152, 219));
            AddPressureSeries(sfChart, "Sensor2", "2号传感器", _viewModel.PressureChartHistory, System.Windows.Media.Color.FromRgb(46, 204, 113));
            AddPressureSeries(sfChart, "Sensor3", "3号传感器", _viewModel.PressureChartHistory, System.Windows.Media.Color.FromRgb(241, 196, 15));
            AddPressureSeries(sfChart, "Sensor4", "4号传感器", _viewModel.PressureChartHistory, System.Windows.Media.Color.FromRgb(230, 126, 34));
            AddPressureSeries(sfChart, "Sensor5", "5号传感器", _viewModel.PressureChartHistory, System.Windows.Media.Color.FromRgb(155, 89, 182));
            AddPressureSeries(sfChart, "Sensor6", "6号传感器", _viewModel.PressureChartHistory, System.Windows.Media.Color.FromRgb(231, 76, 60));
            AddPressureSeries(sfChart, "FusedPressure", "融合权重压力", _viewModel.PressureChartHistory, System.Windows.Media.Color.FromRgb(255, 255, 255), 3);

            sfChart.Legend = new ChartLegend
            {
                DockPosition = ChartDock.Top,
                ItemMargin = new Thickness(5),
                CheckBoxVisibility = Visibility.Collapsed,
                Foreground = Brushes.White
            };

            return sfChart;
        }

        private static void AddPressureSeries(SfChart chart, string yPath, string label, object itemsSource, System.Windows.Media.Color color, double thickness = 2)
        {
            var series = new LineSeries
            {
                XBindingPath = "TimeDisplay",
                YBindingPath = yPath,
                ItemsSource = itemsSource,
                Interior = new SolidColorBrush(color),
                StrokeThickness = thickness,
                Foreground = Brushes.White,
                Label = label,
                AdornmentsInfo = new ChartAdornmentInfo { ShowMarker = false }
            };

            chart.Series.Add(series);
        }

        private Border CreateDeviceStatusPanel()
        {
            var border = CreateSectionBorder("🔧 设备状态");

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10)
            };

            var stack = new StackPanel();

            // 步进电机状态
            stack.Children.Add(new TextBlock { Text = "步进电机", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)), Margin = new Thickness(0, 0, 0, 10) });

            var motorPanel = new ItemsControl();
            motorPanel.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("StepperMotors"));
            motorPanel.ItemTemplate = CreateDeviceItemTemplate("SpeedDisplay");
            stack.Children.Add(motorPanel);

            // 分隔线
            stack.Children.Add(new Separator { Margin = new Thickness(0, 15, 0, 15), Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 70, 80)) });

            // 舵机状态
            stack.Children.Add(new TextBlock { Text = "舵机状态", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)), Margin = new Thickness(0, 0, 0, 10) });

            var servoPanel = new ItemsControl();
            servoPanel.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("Servos"));
            servoPanel.ItemTemplate = CreateDeviceItemTemplate("AngleDisplay");
            stack.Children.Add(servoPanel);

            scrollViewer.Content = stack;
            border.Child = scrollViewer;
            return border;
        }

        private DataTemplate CreateDeviceItemTemplate(string valueBindingPath)
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(Border));

            factory.SetValue(Border.BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            factory.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 8));
            factory.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));

            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            factory.AppendChild(gridFactory);

            var nameColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            nameColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            gridFactory.AppendChild(nameColumn);

            var valueColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            valueColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Auto));
            gridFactory.AppendChild(valueColumn);

            var statusColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            statusColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Auto));
            gridFactory.AppendChild(statusColumn);

            var leftStack = new FrameworkElementFactory(typeof(StackPanel));
            leftStack.SetValue(Grid.ColumnProperty, 0);
            leftStack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            gridFactory.AppendChild(leftStack);

            var idText = new FrameworkElementFactory(typeof(TextBlock));
            idText.SetBinding(TextBlock.TextProperty, new Binding("DeviceName"));
            idText.SetValue(TextBlock.FontSizeProperty, 13.0);
            idText.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            idText.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            leftStack.AppendChild(idText);

            var speedText = new FrameworkElementFactory(typeof(TextBlock));
            speedText.SetBinding(TextBlock.TextProperty, new Binding(valueBindingPath));
            speedText.SetValue(TextBlock.FontSizeProperty, 11.0);
            speedText.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)));
            speedText.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
            leftStack.AppendChild(speedText);

            var statusPanel = new FrameworkElementFactory(typeof(StackPanel));
            statusPanel.SetValue(Grid.ColumnProperty, 2);
            statusPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            statusPanel.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
            gridFactory.AppendChild(statusPanel);

            var indicator = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            indicator.SetValue(System.Windows.Shapes.Ellipse.WidthProperty, 10.0);
            indicator.SetValue(System.Windows.Shapes.Ellipse.HeightProperty, 10.0);
            indicator.SetValue(System.Windows.Shapes.Ellipse.MarginProperty, new Thickness(0, 0, 8, 0));
            indicator.SetBinding(System.Windows.Shapes.Ellipse.FillProperty, new Binding("IsConnected") { Converter = new ConnectionStateToBrushConverter() });
            statusPanel.AppendChild(indicator);

            var statusText = new FrameworkElementFactory(typeof(TextBlock));
            statusText.SetBinding(TextBlock.TextProperty, new Binding("ConnectionStatusDisplay"));
            statusText.SetValue(TextBlock.FontSizeProperty, 11.0);
            statusText.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220)));
            statusPanel.AppendChild(statusText);

            template.VisualTree = factory;
            return template;
        }

        private Border CreateSectionBorder(string title)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(8),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 50, 60)),
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
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                Margin = new Thickness(12, 10, 12, 8)
            };
            Grid.SetRow(titleBlock, 0);
            grid.Children.Add(titleBlock);

            var contentPresenter = new ContentPresenter { Margin = new Thickness(8, 0, 8, 8) };
            Grid.SetRow(contentPresenter, 1);
            grid.Children.Add(contentPresenter);

            border.Child = grid;
            return border;
        }
    }

    // 转换器
    public class ConnectionStateToBrushConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool connected && connected ? Brushes.LawnGreen : Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToHomeConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (bool)value ? "✓ 已归位" : "○ 未归位";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }

    public class ChartModeToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return Equals(value?.ToString(), parameter?.ToString()) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }
}
