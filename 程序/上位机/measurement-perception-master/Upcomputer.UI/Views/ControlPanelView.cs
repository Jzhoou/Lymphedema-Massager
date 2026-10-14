using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection.Metadata;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Upcomputer.UI.ViewModels;
using Upcomputer.Common.Enums;  

namespace Upcomputer.UI.Views
{
    public class ControlPanelView : UserControl
    {
        private readonly ControlPanelViewModel _viewModel;

        public ControlPanelView(ControlPanelViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;
            BuildUserInterface();

            this.IsVisibleChanged += OnIsVisibleChanged;

            this.Unloaded += OnUnloaded;

            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[ControlPanelView] Loaded 事件触发");
            _viewModel.RefreshAllCommands();
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 当视图变为可见时，强制刷新所有命令
            if (this.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("[ControlPanelView] 视图变为可见，刷新命令状态");

                // 立即同步一次
                _viewModel.SyncConnectionState();

                // 使用多个优先级确保刷新
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _viewModel.RefreshAllCommands();
                    CommandManager.InvalidateRequerySuggested();
                }), System.Windows.Threading.DispatcherPriority.Normal);

                // 在 Loaded 优先级再刷新一次
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _viewModel.RefreshAllCommands();
                    CommandManager.InvalidateRequerySuggested();

                    // 强制重新绑定所有命令
                    var buttons = FindVisualChildren<Button>(this);
                    foreach (var button in buttons)
                    {
                        var command = button.Command;
                        button.Command = null;
                        button.Command = command;
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        // 辅助方法：查找所有子控件
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                    yield return t;

                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            this.Unloaded -= OnUnloaded;
        }


        private void BuildUserInterface()
        {
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(15)
            };

            var mainStack = new StackPanel();

            // 设备总控
            mainStack.Children.Add(CreateMainControlPanel());

            // 模式切换
            mainStack.Children.Add(CreateModeSelectionPanel());

            // 运动控制
            mainStack.Children.Add(CreateMotionControlPanel());

            // 电机控制
            mainStack.Children.Add(CreateMotorControlPanel());

            // 按摩参数
            mainStack.Children.Add(CreateMassageParamsPanel());

            // NTP时间同步
            mainStack.Children.Add(CreateNtpSyncPanel());

            // 命令结果
            mainStack.Children.Add(CreateCommandResultPanel());

            scrollViewer.Content = mainStack;
            Content = scrollViewer;
        }

        private Border CreateMainControlPanel()
        {
            var border = CreateSectionBorder("🎮 设备总控");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var startBtn = CreateBigButton("▶ 启动治疗", new SolidColorBrush(Color.FromRgb(46, 204, 113)));
            startBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("StartCommand"));
            startBtn.SetBinding(Button.IsEnabledProperty, new Binding("CanStartTreatment"));
            Grid.SetColumn(startBtn, 0);
            grid.Children.Add(startBtn);

            var stopBtn = CreateBigButton("⏹ 停止治疗", new SolidColorBrush(Color.FromRgb(200, 60, 50)));
            stopBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("StopCommand"));
            stopBtn.SetBinding(Button.IsEnabledProperty, new Binding("CanStopTreatment"));
            Grid.SetColumn(stopBtn, 1);
            grid.Children.Add(stopBtn);

            var pauseResumeBtn = CreateBigButton("⏸ 暂停治疗", new SolidColorBrush(Color.FromRgb(230, 126, 34)));
            pauseResumeBtn.SetBinding(Button.ContentProperty, new Binding("PauseResumeButtonText"));
            pauseResumeBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("PauseResumeCommand"));
            pauseResumeBtn.SetBinding(Button.IsEnabledProperty, new Binding("CanPauseOrResumeTreatment"));
            Grid.SetColumn(pauseResumeBtn, 2);
            grid.Children.Add(pauseResumeBtn);

            var emergencyBtn = CreateBigButton("🆘 紧急停机", new SolidColorBrush(Color.FromRgb(155, 89, 182)));
            emergencyBtn.SetBinding(Button.ContentProperty, new Binding("EmergencyButtonText"));
            emergencyBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("EmergencyStopCommand"));
            emergencyBtn.SetBinding(Button.IsEnabledProperty, new Binding("CanToggleEmergency"));
            Grid.SetColumn(emergencyBtn, 3);
            grid.Children.Add(emergencyBtn);

            var borderGrid = border.Child as Grid;
            Grid.SetRow(grid, 1);
            grid.Margin = new Thickness(12, 0, 12, 12);
            borderGrid?.Children.Add(grid);


            return border;
        }

        private Border CreateModeSelectionPanel()
        {
            var border = CreateSectionBorder("📋 治疗模式");
            var borderGrid = border.Child as Grid;

            var stack = new StackPanel();
            stack.Margin = new Thickness(12, 0, 12, 12);

            var comboBox = new ComboBox
            {
                Height = 35,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 55)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 85, 100)),
                ItemTemplate = CreateModeItemTemplate(),
            };

            comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("TherapyModes"));
            comboBox.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedMode"));
            stack.Children.Add(comboBox);

            Grid.SetRow(stack, 1);
            borderGrid?.Children.Add(stack);

            return border;
        }

        private DataTemplate CreateModeItemTemplate()
        {
            var template = new DataTemplate();
            var textFactory = new FrameworkElementFactory(typeof(TextBlock));

            // 正确写法：用 SetValue，不加 TextBlock.
            textFactory.SetValue(TextBlock.FontSizeProperty, 14.0);
            textFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
            textFactory.SetValue(TextBlock.PaddingProperty, new Thickness(4));

            var binding = new Binding(".") { Converter = new TherapyModeToChineseConverter() };
            textFactory.SetBinding(TextBlock.TextProperty, binding);

            template.VisualTree = textFactory;
            return template;
        }

        public class TherapyModeToChineseConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value == null) return "未知";

                string enumText = value.ToString() ?? "未知";

                return enumText switch
                {
                    "Rehabilitation" => "康复模式",
                    "Massage" => "按摩模式",
                    "Custom" => "自定义模式",
                    _ => enumText
                };
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }


        private Border CreateMotionControlPanel()
        {
            var border = CreateSectionBorder("⚙️ 运动控制");
            var borderGrid = border.Child as Grid;

            var stack = new StackPanel();
            stack.Margin = new Thickness(12, 0, 12, 12);

            // 步进电机控制
            stack.Children.Add(new TextBlock { Text = "步进电机", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)), Margin = new Thickness(0, 0, 0, 10) });

            var motorPanel = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            motorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 标签
            motorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 滑块
            motorPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 数值


            var speedLabel = new TextBlock { Text = "转速:", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(speedLabel, 0);
            motorPanel.Children.Add(speedLabel);

            var speedSlider = new Slider { Minimum = 0, Maximum = 200, TickFrequency = 10, IsSnapToTickEnabled = true, Height = 30 };
            speedSlider.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding("StepperSpeed"));
            Grid.SetColumn(speedSlider, 1);
            motorPanel.Children.Add(speedSlider);

            var speedValue = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)), Margin = new Thickness(10, 0, 0, 0) };
            speedValue.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("StepperSpeed") { StringFormat = "{0:F0} rpm" });
            Grid.SetColumn(speedValue, 2);
            motorPanel.Children.Add(speedValue);

            stack.Children.Add(motorPanel);

            // 舵机控制
            stack.Children.Add(new TextBlock { Text = "舵机角度调节", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)), Margin = new Thickness(0, 0, 0, 10) });

            var servoItems = new ItemsControl();
            servoItems.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("ServoAngles"));
            servoItems.ItemTemplate = CreateAngleSliderTemplate();
            stack.Children.Add(servoItems);

            var homeBtn = new Button
            {
                Content = "🏠 一键归位",
                Height = 35,
                Background = new SolidColorBrush(Color.FromRgb(52, 73, 94)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 15, 0, 0)
            };
            homeBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("HomeAllServosCommand"));
            stack.Children.Add(homeBtn);


            Grid.SetRow(stack, 1);
            borderGrid?.Children.Add(stack);

            return border;
        }

        private Border CreateNtpSyncPanel()
        {
            var border = CreateSectionBorder("⏱ 时间同步");
            var borderGrid = border.Child as Grid;

            var stack = new StackPanel { Margin = new Thickness(12, 0, 12, 12) };

            var ntpBtn = new Button
            {
                Content = "⏱ NTP时间同步",
                Height = 40,
                Background = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            ntpBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("NtpSyncCommand"));
            stack.Children.Add(ntpBtn);

            Grid.SetRow(stack, 1);
            borderGrid?.Children.Add(stack);

            return border;
        }

        private DataTemplate CreateAngleSliderTemplate()
        {
            var template = new DataTemplate();
            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            gridFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));

            // 🔥 唯一正确方式：直接给 Grid 加列，不能用 AppendChild
            Grid grid = new Grid
            {
                ColumnDefinitions =
        {
            new ColumnDefinition { Width = new GridLength(60) },
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            new ColumnDefinition { Width = new GridLength(50) }
        }
            };

            // 把子元素直接加进 grid
            TextBlock label = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetBinding(TextBlock.TextProperty, new Binding("DisplayName"));
            grid.Children.Add(label);

            Slider slider = new Slider
            {
                Minimum = 0,
                Maximum = 180,
                Height = 25
            };
            slider.SetBinding(Slider.ValueProperty, new Binding("Angle"));
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);

            TextBlock valueText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            valueText.SetBinding(TextBlock.TextProperty, new Binding("Angle") { StringFormat = "{0:F0}°" });
            Grid.SetColumn(valueText, 2);
            grid.Children.Add(valueText);

            template.VisualTree = new FrameworkElementFactory(typeof(Grid));
            return template;
        }

        private Border CreateMassageParamsPanel()
        {
            var border = CreateSectionBorder("💆 按摩参数");
            var borderGrid = border.Child as Grid;


            var stack = new StackPanel();
            stack.Margin = new Thickness(12, 0, 12, 12);

            // 强度滑块
            stack.Children.Add(new TextBlock { Text = "按摩强度", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)), Margin = new Thickness(0, 0, 0, 5) });

            var intensityGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            intensityGrid.ColumnDefinitions.Add(new ColumnDefinition());
            intensityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var intensitySlider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, Height = 30 };
            intensitySlider.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding("Intensity"));
            Grid.SetColumn(intensitySlider, 0);
            intensityGrid.Children.Add(intensitySlider);

            var intensityValue = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)), Margin = new Thickness(10, 0, 0, 0) };
            intensityValue.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Intensity") { StringFormat = "{0:F0}%" });
            Grid.SetColumn(intensityValue, 1);
            intensityGrid.Children.Add(intensityValue);

            stack.Children.Add(intensityGrid);

            // 时间选择
            stack.Children.Add(new TextBlock { Text = "按摩时间", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)), Margin = new Thickness(0, 0, 0, 5) });

            var timeGrid = new Grid();
            timeGrid.ColumnDefinitions.Add(new ColumnDefinition());
            timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var timeSlider = new Slider { Minimum = 5, Maximum = 60, TickFrequency = 5, IsSnapToTickEnabled = true, Height = 30 };
            timeSlider.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding("Duration"));
            Grid.SetColumn(timeSlider, 0);
            timeGrid.Children.Add(timeSlider);

            var timeValue = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)), Margin = new Thickness(10, 0, 0, 0) };
            timeValue.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Duration") { StringFormat = "{0:F0} 分钟" });
            Grid.SetColumn(timeValue, 1);
            timeGrid.Children.Add(timeValue);

            stack.Children.Add(timeGrid);

            // 保存按钮
            var saveBtn = new Button
            {
                Content = "💾 保存并下发参数",
                Height = 40,
                Background = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 20, 0, 0)
            };
            saveBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("SaveParametersCommand"));
            stack.Children.Add(saveBtn);

            Grid.SetRow(stack, 1);
            borderGrid?.Children.Add(stack);
            return border;
        }

        private Border CreateMotorControlPanel()
        {
            var border = CreateSectionBorder("🧭 电机控制");
            var borderGrid = border.Child as Grid;

            var stack = new StackPanel
            {
                Margin = new Thickness(12, 0, 12, 12)
            };

            var inputGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var directionPanel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            directionPanel.Children.Add(new TextBlock
            {
                Text = "方向",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            var directionCombo = new ComboBox
            {
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 55)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 85, 100)),
                ItemTemplate = CreateBlackTextItemTemplate()
            };
            directionCombo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("MotorDirectionOptions"));
            directionCombo.SetBinding(Selector.SelectedItemProperty, new Binding("SelectedMotorDirection"));
            directionPanel.Children.Add(directionCombo);
            Grid.SetColumn(directionPanel, 0);
            inputGrid.Children.Add(directionPanel);

            var stepsPanel = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
            stepsPanel.Children.Add(new TextBlock
            {
                Text = "步数",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            var stepsTextBox = new TextBox
            {
                Height = 32,
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 55)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 85, 100))
            };
            stepsTextBox.SetBinding(TextBox.TextProperty, new Binding("MotorStepsInput") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            stepsPanel.Children.Add(stepsTextBox);
            Grid.SetColumn(stepsPanel, 1);
            inputGrid.Children.Add(stepsPanel);

            var speedPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            speedPanel.Children.Add(new TextBlock
            {
                Text = "速度(RPM)",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                Margin = new Thickness(0, 0, 0, 6)
            });
            var speedTextBox = new TextBox
            {
                Height = 32,
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 55)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 85, 100))
            };
            speedTextBox.SetBinding(TextBox.TextProperty, new Binding("MotorSpeedInput") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            speedPanel.Children.Add(speedTextBox);
            Grid.SetColumn(speedPanel, 2);
            inputGrid.Children.Add(speedPanel);

            stack.Children.Add(inputGrid);

            var buttonGrid = new Grid();
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition());
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var homeBtn = new Button
            {
                Content = "🏁 电机回零",
                Height = 36,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(52, 73, 94)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            homeBtn.SetBinding(Button.CommandProperty, new Binding("MotorHomeCommand"));
            Grid.SetColumn(homeBtn, 0);
            buttonGrid.Children.Add(homeBtn);

            var stepBtn = new Button
            {
                Content = "🚀 电机步进运动",
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            stepBtn.SetBinding(Button.CommandProperty, new Binding("MotorStepCommand"));
            Grid.SetColumn(stepBtn, 1);
            buttonGrid.Children.Add(stepBtn);

            stack.Children.Add(buttonGrid);

            Grid.SetRow(stack, 1);
            borderGrid?.Children.Add(stack);
            return border;
        }

        private DataTemplate CreateBlackTextItemTemplate()
        {
            var template = new DataTemplate();
            var textFactory = new FrameworkElementFactory(typeof(TextBlock));
            textFactory.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
            textFactory.SetBinding(TextBlock.TextProperty, new Binding("."));
            template.VisualTree = textFactory;
            return template;
        }

        private Border CreateCommandResultPanel()
        {
            var border = CreateSectionBorder("📝 执行结果");
            var borderGrid = border.Child as Grid;


            var resultText = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.Medium, 
                Foreground = new SolidColorBrush(Color.FromRgb(220, 230, 240)),
                TextWrapping = TextWrapping.Wrap
            };
            resultText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("CommandResult"));

            Grid.SetRow(resultText, 1);
            borderGrid?.Children.Add(resultText);

            return border;
        }

        private Button CreateBigButton(string content, Brush background)
        {
            return new Button
            {
                Content = content,
                Height = 50,
                Margin = new Thickness(5),
                Background = background,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
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


            border.Child = grid;
            return border;
        }
    }
}
