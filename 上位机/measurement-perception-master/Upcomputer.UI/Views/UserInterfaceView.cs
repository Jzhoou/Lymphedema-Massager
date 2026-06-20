using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Upcomputer.UI.ViewModels;
using Upcomputer.Core.Interfaces;

namespace Upcomputer.UI.Views
{
    public class UserInterfaceView : UserControl
    {
        private readonly UserInterfaceViewModel _viewModel;
        private readonly ICommunicationService? _communicationService;

        public UserInterfaceView(UserInterfaceViewModel viewModel, ICommunicationService? communicationService = null)
        {
            _viewModel = viewModel;
            _communicationService = communicationService;
            DataContext = _viewModel;
            BuildUserInterface();

            // View & ViewModel are cached (singletons). When navigating back to this page,
            // reload users so newly added users are visible without restarting the app.
            IsVisibleChanged += async (s, e) =>
            {
                if (IsVisible)
                {
                    await _viewModel.RefreshUsersAsync();
                }
            };
        }

        private void BuildUserInterface()
        {
            var rootGrid = new Grid { Background = new SolidColorBrush(Color.FromRgb(15, 25, 35)) };

            // Selection panel fills the whole area initially
            var selectionBorder = CreateSelectionPanel();
            rootGrid.Children.Add(selectionBorder);

            // Main content, hidden until selection confirmed
            var content = CreateMainContent(); // Prepare for layout improvements
            content.Visibility = _viewModel.IsConfirmed ? Visibility.Visible : Visibility.Collapsed;
            // Listen for changes
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(UserInterfaceViewModel.IsConfirmed))
                {
                    content.Dispatcher.Invoke(() => content.Visibility = _viewModel.IsConfirmed ? Visibility.Visible : Visibility.Collapsed);
                    selectionBorder.Dispatcher.Invoke(() => selectionBorder.Visibility = _viewModel.IsConfirmed ? Visibility.Collapsed : Visibility.Visible);
                }
            };

            rootGrid.Children.Add(content);

            Content = rootGrid;
        }

        private Border CreateSelectionPanel()
        {
            var border = new Border
            {
                Padding = new Thickness(30),
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Use MaxWidth so the panel can shrink on small windows instead of causing overlap.
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            panel.MaxWidth = 840;

            panel.Children.Add(new TextBlock
            {
                Text = "请选择用户登录",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 16),
                TextAlignment = TextAlignment.Center
            });

            var searchHeader = new TextBlock
            {
                Text = "搜索框：",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            panel.Children.Add(searchHeader);

            var searchBox = new TextBox
            {
                Height = 34,
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Foreground = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 85, 100))
            };
            // allow the textbox to stretch within the constrained panel
            searchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            searchBox.SetBinding(TextBox.TextProperty, new Binding("SearchText")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            panel.Children.Add(searchBox);

            var searchBtn = new Button
            {
                Content = "搜索",
                Width = 140,
                Height = 34,
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            searchBtn.SetBinding(Button.CommandProperty, new Binding("SearchCommand"));
            panel.Children.Add(searchBtn);

            var comboBox = new ComboBox
            {
                Height = 36,
                // width not fixed so it can adapt on smaller windows
                DisplayMemberPath = "UserName",
                SelectedValuePath = "UserId",
                ItemsSource = _viewModel.FilteredUsers,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Foreground = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 85, 100))
            };

            comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;

            comboBox.SetBinding(ComboBox.TextProperty, new Binding("SelectedUserDisplayText")
            {
                Mode = BindingMode.OneWay
            });
            comboBox.IsEditable = true;
            comboBox.IsReadOnly = true;

            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
            var highlight = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            highlight.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(200, 230, 250))));
            highlight.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
            itemStyle.Triggers.Add(highlight);
            comboBox.ItemContainerStyle = itemStyle;

            comboBox.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding("SelectedUser") { Mode = System.Windows.Data.BindingMode.TwoWay });
            panel.Children.Add(comboBox);

            var statusText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 12),
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                TextAlignment = TextAlignment.Center
            };
            statusText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("StatusText"));
            panel.Children.Add(statusText);

            var enterBtn = new Button
            {
                Content = "进入",
                Width = 140,
                Height = 38,
                Background = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            enterBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("EnterCommand"));
            panel.Children.Add(enterBtn);

            border.Child = panel;
            return border;
        }

        private Border CreateMainContent()
        {
            // Use a DockPanel to allow header and footer to size to content and center body area, improving layout
            var root = new DockPanel { Margin = new Thickness(12) };

            var header = CreateUserSelector();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var footer = CreateBottomBar();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var contentGrid = CreateContentGrid();
            // make content scrollable so panels don't overlap at small sizes
            var scroll = new ScrollViewer
            {
                Content = contentGrid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            root.Children.Add(scroll);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 25, 35)),
                Child = root,
                Padding = new Thickness(8)
            };

            return border;
        }

        private Border CreateUserSelector()
        {
            var border = CreateSectionBorder("👤 用户界面");
            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.MinHeight = 80;

            stack.Children.Add(new TextBlock
            {
                Text = "当前用户：",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            var txt = new TextBlock { FontSize = 16, Foreground = Brushes.White };
            txt.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("SelectedUser.UserName") );
            stack.Children.Add(txt);

            var infoPanel = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
            infoPanel.Children.Add(CreateUserInfoText("性别：", "SelectedUser.Gender"));
            infoPanel.Children.Add(CreateUserInfoText("身高：", "SelectedUser.HeightCm", "{0:F0} cm"));
            infoPanel.Children.Add(CreateUserInfoText("体重：", "SelectedUser.WeightKg", "{0:F1} kg"));
            infoPanel.Children.Add(CreateUserInfoText("BMI：", "SelectedUser.BmiDisplay"));
            stack.Children.Add(infoPanel);

            // Session summary
            var statusText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255))
            };
            statusText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("StatusText"));
            stack.Children.Add(statusText);

            var exitBtn = new Button
            {
                Content = "退出",
                Width = 140,
                Height = 34,
                Margin = new Thickness(0, 10, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(80, 90, 100)),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            exitBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ExitCommand"));
            stack.Children.Add(exitBtn);

            var treatmentPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var startBtn = new Button
            {
                Content = "▶ 开始治疗",
                Width = 170,
                Height = 42,
                Background = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 14, 0)
            };
            startBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("StartTreatmentCommand"));
            startBtn.SetBinding(Button.IsEnabledProperty, new System.Windows.Data.Binding("CanStartTreatment"));
            treatmentPanel.Children.Add(startBtn);

            var stopBtn = new Button
            {
                Content = "⏹ 停止治疗",
                Width = 170,
                Height = 42,
                Background = new SolidColorBrush(Color.FromRgb(200, 60, 50)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14
            };
            stopBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("StopTreatmentCommand"));
            stopBtn.SetBinding(Button.IsEnabledProperty, new System.Windows.Data.Binding("CanStopTreatment"));
            treatmentPanel.Children.Add(stopBtn);
            treatmentPanel.HorizontalAlignment = HorizontalAlignment.Center;

            stack.Children.Add(treatmentPanel);

            // Make user selector content adapt layout when width is constrained
            var dock = new DockPanel();
            dock.Children.Add(stack);
            border.Child = dock;
            return border;
        }

        private Grid CreateContentGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.9, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.05, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.95, GridUnitType.Star) });

            var leftPanel = CreateLeftPanel();
            Grid.SetRowSpan(leftPanel, 2);
            Grid.SetColumn(leftPanel, 0);
            grid.Children.Add(leftPanel);

            var rightTop = CreateRightMiddlePanel();
            Grid.SetRow(rightTop, 0);
            Grid.SetColumn(rightTop, 1);
            grid.Children.Add(rightTop);

            var rightBottom = CreateRightBottomPanel();
            Grid.SetRow(rightBottom, 1);
            Grid.SetColumn(rightBottom, 1);
            grid.Children.Add(rightBottom);

            return grid;
        }

        private Border CreateLeftPanel()
        {
            var border = CreateSectionBorder("左侧 1/3 - 3D/2.5D 训练区");
            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.MinWidth = 300;

            // 按摩头可视化标题
            stack.Children.Add(new TextBlock
            {
                Text = "按摩头可视化",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var arm3DContainer = new Border
            {
                Height = 350,
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 50)),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 75, 90)),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };
            arm3DContainer.Child = new Arm3DView(_communicationService);
            stack.Children.Add(arm3DContainer);

            // 压力列表绑定
            var pressurePanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var pressureLabel = new TextBlock { Text = "压力传感器", Foreground = Brushes.White, FontWeight = FontWeights.Bold };
            pressurePanel.Children.Add(pressureLabel);

            var pressureList = new ItemsControl();
            var itemTemplate = new DataTemplate();
            var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
            panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding("SensorId") { StringFormat = "传感器 {0}" });
            title.SetValue(TextBlock.WidthProperty, 90.0);
            title.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            panelFactory.AppendChild(title);

            var value = new FrameworkElementFactory(typeof(TextBlock));
            value.SetBinding(TextBlock.TextProperty, new Binding("DisplayValue"));
            value.SetValue(TextBlock.WidthProperty, 80.0);
            value.SetValue(TextBlock.ForegroundProperty, Brushes.Orange);
            panelFactory.AppendChild(value);

            var pb = new FrameworkElementFactory(typeof(ProgressBar));
            pb.SetBinding(ProgressBar.ValueProperty, new Binding("CurrentValue"));
            pb.SetBinding(ProgressBar.MaximumProperty, new Binding("MaxValue"));
            pb.SetValue(ProgressBar.HeightProperty, 12.0);
            pb.SetValue(ProgressBar.WidthProperty, 140.0);
            panelFactory.AppendChild(pb);

            itemTemplate.VisualTree = panelFactory;
            pressureList.ItemTemplate = itemTemplate;
            pressureList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("PressureDataList"));

            pressurePanel.Children.Add(pressureList);
            stack.Children.Add(pressurePanel);

            border.Child = stack;
            return border;
        }

        private static TextBlock CreateUserInfoText(string label, string bindingPath, string? format = null)
        {
            var textBlock = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(200, 210, 220)),
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var valueFormat = string.IsNullOrWhiteSpace(format) ? "{0}" : format;
            textBlock.SetBinding(TextBlock.TextProperty, new Binding(bindingPath)
            {
                StringFormat = label + valueFormat
            });
            return textBlock;
        }

        private Border CreateRightMiddlePanel()
        {
            var border = CreateSectionBorder("右中 - 水肿预测");
            var grid = new Grid { Margin = new Thickness(12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var edemaPanel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            var edemaTitle = new TextBlock { Text = "水肿预测", Foreground = Brushes.White, FontWeight = FontWeights.Bold };
            edemaPanel.Children.Add(edemaTitle);

            var severityText = new TextBlock
            {
                FontSize = 24,
                Foreground = Brushes.White,
                MinHeight = 42
            };
            severityText.SetBinding(TextBlock.TextProperty, new Binding("EdemaPredictionResult.Severity") { FallbackValue = "--" });
            edemaPanel.Children.Add(severityText);

            var detailText = new TextBlock
            {
                FontSize = 18,
                Foreground = Brushes.Orange,
                MinHeight = 32,
                TextWrapping = TextWrapping.Wrap
            };
            detailText.SetBinding(TextBlock.TextProperty, new Binding("EdemaPredictionResult.SeverityDescription") { FallbackValue = "--" });
            edemaPanel.Children.Add(detailText);

            var probText = new TextBlock
            {
                FontSize = 16,
                Foreground = Brushes.LightBlue,
                Margin = new Thickness(0, 4, 0, 0)
            };
            probText.SetBinding(TextBlock.TextProperty, new Binding("EdemaPredictionResult.Probability") { StringFormat = "概率：{0:P1}", FallbackValue = "概率：--" });
            edemaPanel.Children.Add(probText);

            Grid.SetRow(edemaPanel, 0);
            grid.Children.Add(edemaPanel);

            border.Child = grid;
            return border;
        }

        private Border CreateRightBottomPanel()
        {
            var border = CreateSectionBorder("右下 - 依从性日历");
            var stack = new StackPanel { Margin = new Thickness(12) };

            var monthTitle = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            BindingOperations.SetBinding(monthTitle, TextBlock.TextProperty, new Binding("CalendarMonthTitle"));
            stack.Children.Add(monthTitle);

            var weekHeader = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var dayName in new[] { "日", "一", "二", "三", "四", "五", "六" })
            {
                weekHeader.Children.Add(new TextBlock
                {
                    Text = dayName,
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                });
            }

            stack.Children.Add(weekHeader);

            var calendar = new ItemsControl();
            var itemsPanelFactory = new FrameworkElementFactory(typeof(UniformGrid));
            itemsPanelFactory.SetValue(UniformGrid.ColumnsProperty, 7);
            calendar.ItemsPanel = new ItemsPanelTemplate(itemsPanelFactory);

            var template = new DataTemplate();
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.WidthProperty, 32.0);
            borderFactory.SetValue(Border.HeightProperty, 32.0);
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.MarginProperty, new Thickness(2));
            borderFactory.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            borderFactory.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.SetBinding(Border.BackgroundProperty, new Binding("Status") { Converter = new CalendarDayBackgroundConverter() });
            borderFactory.SetBinding(Border.ToolTipProperty, new Binding("TooltipText"));

            var textFactory = new FrameworkElementFactory(typeof(TextBlock));
            textFactory.SetBinding(TextBlock.TextProperty, new Binding("DayText"));
            textFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textFactory.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            textFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
            borderFactory.AppendChild(textFactory);
            template.VisualTree = borderFactory;

            calendar.ItemTemplate = template;
            calendar.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("CalendarDays"));
            stack.Children.Add(calendar);

            var legendPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            legendPanel.Children.Add(CreateCalendarLegendItem(Color.FromRgb(46, 204, 113), "已按摩"));
            legendPanel.Children.Add(CreateCalendarLegendItem(Color.FromRgb(231, 76, 60), "未按摩"));
            legendPanel.Children.Add(CreateCalendarLegendItem(Color.FromRgb(70, 80, 90), "未到日期"));
            stack.Children.Add(legendPanel);

            stack.Children.Add(CreateAdviceCard("👨‍⚕️ 医生建议", "DoctorAdvice"));
            stack.Children.Add(CreateAdviceCard("⏰ 康复建议", "RehabilitationReminder"));

            border.Child = stack;
            return border;
        }

        private static UIElement CreateCalendarLegendItem(Color color, string text)
        {
            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 14, 0)
            };
            item.Children.Add(new Border
            {
                Width = 12,
                Height = 12,
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 2, 6, 0)
            });
            item.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                FontSize = 12
            });
            return item;
        }

        private Border CreateAdviceCard(string title, string bindingPath)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 50)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 75, 90)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(10)
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var content = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(200, 210, 220)),
                TextWrapping = TextWrapping.Wrap
            };
            content.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            panel.Children.Add(content);

            card.Child = panel;
            return card;
        }

        private Border CreateBottomBar()
        {
            var border = CreateSectionBorder("底部 - 治疗进度");
            var stack = new StackPanel { Margin = new Thickness(12) };

            var timeline = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = CreateTimelinePanel()
            };
            stack.Children.Add(timeline);

            border.Child = stack;
            return border;
        }

        private StackPanel CreateTimelinePanel()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var card = new Border
            {
                Width = 320,
                Height = 72,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 50)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12)
            };

            var inner = new StackPanel();
            inner.Children.Add(new TextBlock
            {
                Text = "治疗时间",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            });

            var elapsedText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                FontSize = 18
            };
            BindingOperations.SetBinding(elapsedText, TextBlock.TextProperty, new Binding("CurrentSession.ElapsedTimeDisplay"));
            inner.Children.Add(elapsedText);

            var progress = new ProgressBar
            {
                Height = 8,
                Margin = new Thickness(0, 6, 0, 0)
            };
            BindingOperations.SetBinding(progress, ProgressBar.ValueProperty, new Binding("CurrentSession.Progress"));
            inner.Children.Add(progress);

            card.Child = inner;
            panel.Children.Add(card);

            var remainingText = new TextBlock
            {
                Margin = new Thickness(0, 18, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220))
            };
            BindingOperations.SetBinding(remainingText, TextBlock.TextProperty, new Binding("CurrentSession.RemainingTimeDisplay") { StringFormat = "剩余时间：{0}" });
            panel.Children.Add(remainingText);

            return panel;
        }

        private Border CreateRingStatus(string title, Brush brush)
        {
            var border = new Border
            {
                Width = 120,
                Height = 64,
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 50)),
                CornerRadius = new CornerRadius(10),
                BorderBrush = brush,
                BorderThickness = new Thickness(2)
            };

            border.Child = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            return border;
        }

        private Border CreatePlaceholderBlock(string text, double height, bool showProgress = false)
        {
            var border = new Border
            {
                Height = height,
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 50)),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 75, 90)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            if (showProgress)
            {
                stack.Children.Add(new ProgressBar
                {
                    Height = 10,
                    Margin = new Thickness(0, 12, 0, 0),
                    Minimum = 0,
                    Maximum = 100,
                    Value = 60
                });
                stack.Children.Add(new TextBlock
                {
                    Text = "治疗时间：00:20:00",
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                    Margin = new Thickness(0, 8, 0, 0),
                    TextAlignment = TextAlignment.Center
                });
            }

            border.Child = stack;
            return border;
        }

        private static Border CreateSectionBorder(string title)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(12, 10, 12, 8)
            });

            border.Child = grid;
            return border;
        }

        private Border CreateTrendChart()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 50)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 10, 0)
            };

            var canvas = new Canvas { Height = 210 };
            var points = new List<Point>
            {
                new(10, 170), new(60, 150), new(110, 128), new(160, 105), new(210, 92)
            };

            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                StrokeThickness = 3,
                Points = new PointCollection(points)
            };
            canvas.Children.Add(polyline);

            canvas.Children.Add(new TextBlock
            {
                Text = "肿胀改善趋势图",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            border.Child = canvas;
            return border;
        }

        private sealed class CalendarDayBackgroundConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value is UserInterfaceViewModel.CalendarDayStatus status)
                {
                    return status switch
                    {
                        UserInterfaceViewModel.CalendarDayStatus.Completed => new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                        UserInterfaceViewModel.CalendarDayStatus.Missed => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                        _ => new SolidColorBrush(Color.FromRgb(70, 80, 90))
                    };
                }

                return new SolidColorBrush(Color.FromRgb(50, 60, 70));
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => Binding.DoNothing;
        }
    }
}
