using Syncfusion.UI.Xaml.Charts;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.ComponentModel;
using System.Text;
using System.Windows.Media.Animation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Upcomputer.UI.ViewModels;

namespace Upcomputer.UI.Views
{
    public class HistoryView : UserControl
    {
        private readonly HistoryViewModel _viewModel;
        private readonly Border _rightPanelHost = new();
        private readonly Border _detailOverlayHost = new();

        public HistoryView(HistoryViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            PreviewMouseWheel += OnPreviewMouseWheel;
            BuildUserInterface();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(HistoryViewModel.IsDetailViewVisible))
            {
                RefreshDetailOverlay();
                return;
            }

            if (e.PropertyName is nameof(HistoryViewModel.SelectedDetailChartMode))
            {
                RefreshDetailOverlay();
            }
        }

        private void BuildUserInterface()
        {
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            // 搜索栏
            var searchBar = CreateSearchBar();
            Grid.SetRow(searchBar, 0);
            Grid.SetColumnSpan(searchBar, 2);
            mainGrid.Children.Add(searchBar);

            // 历史数据表格
            var dataGrid = CreateDataGrid();
            Grid.SetRow(dataGrid, 1);
            Grid.SetColumn(dataGrid, 0);
            mainGrid.Children.Add(dataGrid);

            // 右侧面板
            var rightPanel = CreateRightPanelHost();
            Grid.SetRow(rightPanel, 1);
            Grid.SetColumn(rightPanel, 1);
            mainGrid.Children.Add(rightPanel);

            // 底部状态栏
            var statusBar = CreateStatusBar();
            Grid.SetRow(statusBar, 2);
            Grid.SetColumnSpan(statusBar, 2);
            mainGrid.Children.Add(statusBar);

            // 整页详情覆盖层
            _detailOverlayHost.Visibility = Visibility.Collapsed;
            _detailOverlayHost.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 28, 38));
            Grid.SetRowSpan(_detailOverlayHost, 3);
            Grid.SetColumnSpan(_detailOverlayHost, 2);
            Panel.SetZIndex(_detailOverlayHost, 10);
            mainGrid.Children.Add(_detailOverlayHost);

            Content = mainGrid;
            RefreshRightPanel();
            RefreshDetailOverlay();
        }

        private Border CreateSearchBar()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(8, 8, 8, 0),
                Padding = new Thickness(12),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            stack.Children.Add(new TextBlock { Text = "开始日期:", FontSize = 13, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });

            var startDatePicker = new DatePicker { Width = 150, Margin = new Thickness(0, 0, 15, 0) };
            startDatePicker.SetBinding(DatePicker.SelectedDateProperty, new System.Windows.Data.Binding("StartDate"));
            stack.Children.Add(startDatePicker);

            stack.Children.Add(new TextBlock { Text = "结束日期:", FontSize = 13, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });

            var endDatePicker = new DatePicker { Width = 150, Margin = new Thickness(0, 0, 15, 0) };
            endDatePicker.SetBinding(DatePicker.SelectedDateProperty, new System.Windows.Data.Binding("EndDate"));
            stack.Children.Add(endDatePicker);

            var searchBtn = new Button
            {
                Content = "🔍 查询",
                Width = 80,
                Height = 28,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            searchBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("SearchCommand"));
            stack.Children.Add(searchBtn);

            var exportBtn = new Button
            {
                Content = "📤 导出",
                Width = 80,
                Height = 28,
                Margin = new Thickness(10, 0, 0, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 73, 94)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            exportBtn.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("ExportCommand"));
            stack.Children.Add(exportBtn);

            border.Child = stack;
            return border;
        }

        private Border CreateDataGrid()
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
            DataGrid dataGrid = null!;

            var headerRow = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };

            var titleBlock = new TextBlock
            {
                Text = "📋 治疗记录",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(titleBlock, Dock.Left);
            headerRow.Children.Add(titleBlock);

            var detailButton = new Button
            {
                Content = "查看详情",
                Padding = new Thickness(12, 4, 12, 4),
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            detailButton.Click += (_, _) => OpenSelectedDetailFromGrid(dataGrid);
            DockPanel.SetDock(detailButton, Dock.Right);
            headerRow.Children.Add(detailButton);

            var deleteButton = new Button
            {
                Content = "删除记录",
                Padding = new Thickness(12, 4, 12, 4),
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            deleteButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("DeleteCommand"));
            DockPanel.SetDock(deleteButton, Dock.Right);
            headerRow.Children.Add(deleteButton);

            Grid.SetRow(headerRow, 0);
            grid.Children.Add(headerRow);

            dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Margin = new Thickness(8, 0, 8, 8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)),
                RowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)),
                AlternatingRowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 45, 55)),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 230, 240)), 
                BorderThickness = new Thickness(0),
                HeadersVisibility = DataGridHeadersVisibility.Column,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Single
            };
            dataGrid.SetBinding(DataGrid.ItemsSourceProperty, new System.Windows.Data.Binding("TreatmentRecords"));
            dataGrid.SetBinding(DataGrid.SelectedItemProperty, new System.Windows.Data.Binding("SelectedRecord"));
            dataGrid.MouseDoubleClick += OnDataGridMouseDoubleClick;

            dataGrid.Columns.Add(new DataGridTextColumn { Header = "时间", Binding = new System.Windows.Data.Binding("StartTimeDisplay"), Width = 150 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "用户名", Binding = new System.Windows.Data.Binding("UserName"), Width = 120 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "模式", Binding = new System.Windows.Data.Binding("Mode"), Width = 100 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "时长", Binding = new System.Windows.Data.Binding("Duration"), Width = 80 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "强度", Binding = new System.Windows.Data.Binding("Intensity"), Width = 80 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "完成率", Binding = new System.Windows.Data.Binding("CompletionRate"), Width = 80 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new System.Windows.Data.Binding("Status"), Width = 80 });

            // 同时需要设置列标题样式
            dataGrid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
            {
                Setters =
                {
                    new Setter(BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 30, 40))),
                    new Setter(ForegroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255))),
                    new Setter(FontWeightProperty, FontWeights.Bold),
                    new Setter(BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
                    new Setter(BorderBrushProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 50, 60)))
                }
            };

            Grid.SetRow(dataGrid, 1);
            grid.Children.Add(dataGrid);

            border.Child = grid;
            return border;
        }

        private Border CreateRightPanelHost()
        {
            _rightPanelHost.Margin = new Thickness(0, 8, 8, 8);
            _rightPanelHost.MinWidth = 420;
            return _rightPanelHost;
        }

        private void RefreshRightPanel()
        {
            if (_rightPanelHost == null)
            {
                return;
            }

            _rightPanelHost.Child = CreateDefaultRightPanel();
        }

        private void RefreshDetailOverlay()
        {
            if (_detailOverlayHost == null)
            {
                return;
            }

            _detailOverlayHost.Visibility = _viewModel.IsDetailViewVisible ? Visibility.Visible : Visibility.Collapsed;
            _detailOverlayHost.Child = _viewModel.IsDetailViewVisible ? CreateDetailPanel() : null;
        }

        private StackPanel CreateDefaultRightPanel()
        {
            var stack = new StackPanel { Margin = new Thickness(0, 8, 8, 8) };

            // 医生建议
            var adviceBorder = CreateInfoCard("👨‍⚕️ 医生建议");
            var adviceText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 210, 220)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 0, 12, 12)
            };
            adviceText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DoctorAdvice"));
            adviceBorder.Child = CreateCardContent("👨‍⚕️ 医生建议", adviceText);
            stack.Children.Add(adviceBorder);

            // 康复提醒
            var reminderBorder = CreateInfoCard("⏰ 康复提醒");
            var reminderText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 210, 220)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 0, 12, 12)
            };
            reminderText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("RehabilitationReminder"));
            reminderBorder.Child = CreateCardContent("⏰ 康复提醒", reminderText);
            stack.Children.Add(reminderBorder);

            return stack;
        }

        private Border CreateDetailPanel()
        {
            var outer = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(16),
                MinWidth = 900,
                MinHeight = 650
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new StackPanel()
            };

            var stack = (StackPanel)scroll.Content;

            var titleRow = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };
            var backButton = new Button
            {
                Content = "← 返回",
                Width = 70,
                Height = 28
            };
            backButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("BackCommand"));
            DockPanel.SetDock(backButton, Dock.Left);
            titleRow.Children.Add(backButton);

            var exportButton = new Button
            {
                Content = "📤 导出数据",
                Width = 90,
                Height = 28,
                Margin = new Thickness(8, 0, 0, 0)
            };
            exportButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("DetailExportCommand"));
            DockPanel.SetDock(exportButton, Dock.Right);
            titleRow.Children.Add(exportButton);

            var reportExportButton = new Button
            {
                Content = "🧾 导出报表",
                Width = 90,
                Height = 28,
                Margin = new Thickness(8, 0, 0, 0)
            };
            reportExportButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("DetailReportExportCommand"));
            DockPanel.SetDock(reportExportButton, Dock.Right);
            titleRow.Children.Add(reportExportButton);

            var titleBlock = new TextBlock
            {
                Text = "📄 会话详情",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            titleRow.Children.Add(titleBlock);
            stack.Children.Add(titleRow);

            var summaryText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 210, 220)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 0, 12, 12)
            };
            summaryText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("SelectedSessionSummary"));
            stack.Children.Add(summaryText);

            var commandCard = CreateDetailCard("控制指令", CreateCommandLogGrid());
            stack.Children.Add(commandCard);

            var contentGrid = new Grid { Margin = new Thickness(12, 0, 12, 12) };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var pressureCard = CreateDetailCard("压力日志", CreatePressureLogGrid());
            var edemaCard = CreateDetailCard("水肿日志", CreateEdemaLogGrid());

            var logGrid = new Grid();
            logGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            logGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(pressureCard, 0);
            Grid.SetColumn(edemaCard, 1);
            logGrid.Children.Add(pressureCard);
            logGrid.Children.Add(edemaCard);
            Grid.SetRow(logGrid, 0);
            contentGrid.Children.Add(logGrid);

            var curveCard = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(0)
            };

            curveCard.Child = CreateDetailCurveSection();
            Grid.SetRow(curveCard, 1);
            contentGrid.Children.Add(curveCard);

            stack.Children.Add(contentGrid);

            outer.Child = scroll;
            return outer;
        }

        private Border CreateDetailCard(string title, UIElement content)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                Margin = new Thickness(12, 10, 12, 8)
            };
            Grid.SetRow(titleBlock, 0);
            grid.Children.Add(titleBlock);

            Grid.SetRow(content, 1);
            grid.Children.Add(content);

            border.Child = grid;
            return border;
        }

        private UIElement CreatePressureLogGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Margin = new Thickness(8, 0, 8, 8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)),
                RowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)),
                AlternatingRowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 45, 55)),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 230, 240)),
                BorderThickness = new Thickness(0),
                HeadersVisibility = DataGridHeadersVisibility.Column,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                MinHeight = 260,
                MaxHeight = 320
            };

            grid.SetBinding(DataGrid.ItemsSourceProperty, new System.Windows.Data.Binding("PressureLogItems"));
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("时间", _viewModel.PressureColumnOptions[0]), Binding = new System.Windows.Data.Binding("TimeDisplay"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("传感器", _viewModel.PressureColumnOptions[1]), Binding = new System.Windows.Data.Binding("SensorId"), Width = 60 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("当前值", _viewModel.PressureColumnOptions[2]), Binding = new System.Windows.Data.Binding("ValueDisplay"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("最大值", _viewModel.PressureColumnOptions[3]), Binding = new System.Windows.Data.Binding("MaxDisplay"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("阈值", _viewModel.PressureColumnOptions[4]), Binding = new System.Windows.Data.Binding("ThresholdDisplay"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("告警", _viewModel.PressureColumnOptions[5]), Binding = new System.Windows.Data.Binding("IsAlert") { StringFormat = "{0}" }, Width = 60 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("延迟(ms)", _viewModel.PressureColumnOptions[6]), Binding = new System.Windows.Data.Binding("LatencyDisplay"), Width = 85 });
            grid.ColumnHeaderStyle = CreateColumnHeaderStyle();
            grid.Loaded += (_, _) => AttachLazyLoadScrollHandler(grid, DetailGridKind.Pressure);

            var stack = new StackPanel();
            stack.Children.Add(grid);
            stack.Children.Add(CreateLoadingFooter("IsPressureLogsLoadingMore", "正在加载更多压力数据..."));
            return stack;
        }

        private UIElement CreateEdemaLogGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Margin = new Thickness(8, 0, 8, 8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)),
                RowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)),
                AlternatingRowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 45, 55)),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 230, 240)),
                BorderThickness = new Thickness(0),
                HeadersVisibility = DataGridHeadersVisibility.Column,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                MinHeight = 260,
                MaxHeight = 320
            };

            grid.SetBinding(DataGrid.ItemsSourceProperty, new System.Windows.Data.Binding("EdemaLogItems"));
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("时间", _viewModel.EdemaColumnOptions[0]), Binding = new System.Windows.Data.Binding("TimeDisplay"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("传感器", _viewModel.EdemaColumnOptions[1]), Binding = new System.Windows.Data.Binding("SensorId"), Width = 60 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("阻抗", _viewModel.EdemaColumnOptions[2]), Binding = new System.Windows.Data.Binding("ImpedanceDisplay"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("水肿百分比", _viewModel.EdemaColumnOptions[3]), Binding = new System.Windows.Data.Binding("PercentageDisplay"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("阈值", _viewModel.EdemaColumnOptions[4]), Binding = new System.Windows.Data.Binding("ThresholdDisplay"), Width = 80 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("告警", _viewModel.EdemaColumnOptions[5]), Binding = new System.Windows.Data.Binding("IsAlert") { StringFormat = "{0}" }, Width = 60 });
            grid.Columns.Add(new DataGridTextColumn { Header = CreateColumnHeader("延迟(ms)", _viewModel.EdemaColumnOptions[6]), Binding = new System.Windows.Data.Binding("LatencyDisplay"), Width = 85 });
            grid.ColumnHeaderStyle = CreateColumnHeaderStyle();
            grid.Loaded += (_, _) => AttachLazyLoadScrollHandler(grid, DetailGridKind.Edema);

            var stack = new StackPanel();
            stack.Children.Add(grid);
            stack.Children.Add(CreateLoadingFooter("IsEdemaLogsLoadingMore", "正在加载更多水肿数据..."));
            return stack;
        }

        private static UIElement CreateColumnHeader(string title, object option)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center
            });

            var checkBox = new CheckBox
            {
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            checkBox.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsChecked")
            {
                Source = option,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });

            panel.Children.Add(checkBox);
            return panel;
        }

        private UIElement CreateCommandLogGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Margin = new Thickness(8, 0, 8, 8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)),
                RowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 40, 50)),
                AlternatingRowBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 45, 55)),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 230, 240)),
                BorderThickness = new Thickness(0),
                HeadersVisibility = DataGridHeadersVisibility.Column,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                MinHeight = 120,
                MaxHeight = 180
            };

            grid.SetBinding(DataGrid.ItemsSourceProperty, new System.Windows.Data.Binding("CommandLogItems"));
            grid.Columns.Add(new DataGridTextColumn { Header = "时间", Binding = new System.Windows.Data.Binding("TimeDisplay"), Width = 85 });
            grid.Columns.Add(new DataGridTextColumn { Header = "指令", Binding = new System.Windows.Data.Binding("CommandName"), Width = 120 });
            grid.Columns.Add(new DataGridTextColumn { Header = "详情", Binding = new System.Windows.Data.Binding("CommandDetails"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.ColumnHeaderStyle = CreateColumnHeaderStyle();
            grid.Loaded += (_, _) => AttachLazyLoadScrollHandler(grid, DetailGridKind.Command);

            var stack = new StackPanel();
            stack.Children.Add(grid);
            stack.Children.Add(CreateLoadingFooter("IsCommandLogsLoadingMore", "正在加载更多控制指令..."));
            return stack;
        }

        private UIElement CreateDetailCurveSection()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };
            var title = new TextBlock
            {
                Text = "📈 会话曲线",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            var selector = new ComboBox
            {
                Width = 140,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            selector.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("DetailChartModes"));
            selector.SetBinding(Selector.SelectedItemProperty, new System.Windows.Data.Binding("SelectedDetailChartMode") { Mode = System.Windows.Data.BindingMode.TwoWay });
            DockPanel.SetDock(selector, Dock.Right);
            header.Children.Add(selector);

            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var chartHost = new Grid { Margin = new Thickness(8, 0, 8, 8) };
            chartHost.Children.Add(CreateDetailChart());
            Grid.SetRow(chartHost, 1);
            grid.Children.Add(chartHost);

            return grid;
        }

        private UIElement CreateDetailChart()
        {
            return _viewModel.SelectedDetailChartMode == "压力数据"
                ? CreatePressureDetailChart()
                : CreateEdemaDetailChart();
        }

        private SfChart CreateEdemaDetailChart()
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
                Header = "水肿百分比 (%)",
                HeaderStyle = new LabelStyle { FontSize = 12, Foreground = Brushes.White },
                LabelStyle = new LabelStyle { FontSize = 10, Foreground = Brushes.White },
                ShowGridLines = true
            };

            var series = new LineSeries
            {
                XBindingPath = "TimeDisplay",
                YBindingPath = "Value",
                ItemsSource = _viewModel.SelectedEdemaCurve,
                Interior = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                StrokeThickness = 2,
                Foreground = Brushes.White,
                Label = "当前数据"
            };
            series.AdornmentsInfo = new ChartAdornmentInfo { ShowMarker = true, Symbol = ChartSymbol.Ellipse };
            sfChart.Series.Add(series);
            sfChart.Legend = new ChartLegend
            {
                DockPosition = ChartDock.Top,
                ItemMargin = new Thickness(5),
                CheckBoxVisibility = Visibility.Collapsed,
                Foreground = Brushes.White
            };

            return sfChart;
        }

        private SfChart CreatePressureDetailChart()
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

            AddPressureDetailSeries(sfChart, "Sensor1", "1号传感器", _viewModel.SelectedPressureChartHistory, System.Windows.Media.Color.FromRgb(52, 152, 219));
            AddPressureDetailSeries(sfChart, "Sensor2", "2号传感器", _viewModel.SelectedPressureChartHistory, System.Windows.Media.Color.FromRgb(46, 204, 113));
            AddPressureDetailSeries(sfChart, "Sensor3", "3号传感器", _viewModel.SelectedPressureChartHistory, System.Windows.Media.Color.FromRgb(241, 196, 15));
            AddPressureDetailSeries(sfChart, "Sensor4", "4号传感器", _viewModel.SelectedPressureChartHistory, System.Windows.Media.Color.FromRgb(230, 126, 34));
            AddPressureDetailSeries(sfChart, "Sensor5", "5号传感器", _viewModel.SelectedPressureChartHistory, System.Windows.Media.Color.FromRgb(155, 89, 182));
            AddPressureDetailSeries(sfChart, "Sensor6", "6号传感器", _viewModel.SelectedPressureChartHistory, System.Windows.Media.Color.FromRgb(231, 76, 60));
            AddPressureDetailSeries(sfChart, "FusedPressure", "融合权重压力", _viewModel.SelectedPressureChartHistory, System.Windows.Media.Color.FromRgb(255, 255, 255), 3);

            sfChart.Legend = new ChartLegend
            {
                DockPosition = ChartDock.Top,
                ItemMargin = new Thickness(5),
                CheckBoxVisibility = Visibility.Collapsed,
                Foreground = Brushes.White
            };

            return sfChart;
        }

        private static void AddPressureDetailSeries(SfChart chart, string yPath, string label, object itemsSource, System.Windows.Media.Color color, double thickness = 2)
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

        private Border CreateCurveChart()
        {
            // 创建外层 Border（带标题）
            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 标题
            var titleBlock = new TextBlock
            {
                Text = "📈 会话曲线（水肿百分比）",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                Margin = new Thickness(12, 10, 12, 8)
            };
            Grid.SetRow(titleBlock, 0);
            grid.Children.Add(titleBlock);

            // SfChart 图表
            var sfChart = new SfChart
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45)),
                Margin = new Thickness(8, 0, 8, 8)
            };

            // 配置 X 轴（类别轴 - 时间）
            sfChart.PrimaryAxis = new CategoryAxis
            {
                LabelStyle = new LabelStyle
                {
                    FontSize = 10,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220))
                },
                ShowGridLines = false
            };

            // 配置 Y 轴（数值轴 - 水肿百分比）
            sfChart.SecondaryAxis = new NumericalAxis
            {
                LabelStyle = new LabelStyle
                {
                    FontSize = 10,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220))
                },
                ShowGridLines = true
            };

            // 添加折线系列
            var lineSeries = new LineSeries
            {
                ItemsSource = _viewModel.SelectedSessionCurve,
                XBindingPath = "TimeDisplay",
                YBindingPath = "Value",
                Interior = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                StrokeThickness = 2,
                Label = "曲线"
            };

            // 配置数据点标记
            lineSeries.AdornmentsInfo = new ChartAdornmentInfo
            {
                ShowMarker = true,
                //MarkerHeight = 6,
                //MarkerWidth = 6,
                Symbol = ChartSymbol.Ellipse,
                //MarkerInterior = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                //MarkerStrokeThickness = 1
            };

            sfChart.Series.Add(lineSeries);

            Grid.SetRow(sfChart, 1);
            grid.Children.Add(sfChart);

            border.Child = grid;
            return border;
        }

        private static Style CreateColumnHeaderStyle()
        {
            return new Style(typeof(DataGridColumnHeader))
            {
                Setters =
                {
                    new Setter(BackgroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 30, 40))),
                    new Setter(ForegroundProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255))),
                    new Setter(FontWeightProperty, FontWeights.Bold),
                    new Setter(BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
                    new Setter(BorderBrushProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 50, 60)))
                }
            };
        }

        private UIElement CreateLoadingFooter(string isLoadingBinding, string text)
        {
            var border = new Border
            {
                Margin = new Thickness(8, 0, 8, 8),
                Padding = new Thickness(12, 8, 12, 8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 38, 48)),
                CornerRadius = new CornerRadius(6)
            };

            border.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(isLoadingBinding)
            {
                Converter = new System.Windows.Controls.BooleanToVisibilityConverter()
            });

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var spinner = new Viewbox
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new Path
                {
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Fill,
                    Data = Geometry.Parse("M 10,2 A 8,8 0 1 1 9.99,2"),
                    Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                    StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeDashArray = new DoubleCollection { 1.2, 2.2 },
                    RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(0)
                }
            };
            spinner.Loaded += (_, _) =>
            {
                if (spinner.Child is Path path && path.RenderTransform is RotateTransform rotateTransform)
                {
                    rotateTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation
                    {
                        From = 0,
                        To = 360,
                        Duration = TimeSpan.FromSeconds(1),
                        RepeatBehavior = RepeatBehavior.Forever
                    });
                }
            };

            panel.Children.Add(spinner);

            panel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220)),
                VerticalAlignment = VerticalAlignment.Center
            });

            border.Child = panel;
            return border;
        }

        private void OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var scrollViewer = FindParent<ScrollViewer>(e.OriginalSource as DependencyObject);
            if (scrollViewer == null)
            {
                return;
            }

            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T target)
                {
                    return target;
                }

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null)
            {
                return null;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T target)
                {
                    return target;
                }

                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private void AttachLazyLoadScrollHandler(DataGrid grid, DetailGridKind gridKind)
        {
            var scrollViewer = FindDescendant<ScrollViewer>(grid);
            if (scrollViewer == null)
            {
                return;
            }

            scrollViewer.ScrollChanged -= OnPressureDetailGridScrollChanged;
            scrollViewer.ScrollChanged -= OnEdemaDetailGridScrollChanged;
            scrollViewer.ScrollChanged -= OnCommandDetailGridScrollChanged;

            if (gridKind == DetailGridKind.Pressure)
            {
                scrollViewer.ScrollChanged += OnPressureDetailGridScrollChanged;
            }
            else if (gridKind == DetailGridKind.Edema)
            {
                scrollViewer.ScrollChanged += OnEdemaDetailGridScrollChanged;
            }
            else
            {
                scrollViewer.ScrollChanged += OnCommandDetailGridScrollChanged;
            }
        }

        private void OnPressureDetailGridScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer viewer && viewer.VerticalOffset >= viewer.ScrollableHeight - 10)
            {
                _ = _viewModel.LoadMorePressureLogsAsync();
            }
        }

        private void OnEdemaDetailGridScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer viewer && viewer.VerticalOffset >= viewer.ScrollableHeight - 10)
            {
                _ = _viewModel.LoadMoreEdemaLogsAsync();
            }
        }

        private void OnCommandDetailGridScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (sender is ScrollViewer viewer && viewer.VerticalOffset >= viewer.ScrollableHeight - 10)
            {
                _ = _viewModel.LoadMoreCommandLogsAsync();
            }
        }

        private Border CreateInfoCard(string title)
        {
            return new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 35, 45)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1)
            };
        }

        private Border CreateStatusBar()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 30, 40)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1, 1, 1, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(8, 0, 8, 8)
            };

            var dock = new DockPanel();

            var pagePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var prevButton = new Button
            {
                Content = "上一页",
                Width = 70,
                Margin = new Thickness(0, 0, 8, 0)
            };
            prevButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("PreviousPageCommand"));
            pagePanel.Children.Add(prevButton);

            var nextButton = new Button
            {
                Content = "下一页",
                Width = 70,
                Margin = new Thickness(0, 0, 12, 0)
            };
            nextButton.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding("NextPageCommand"));
            pagePanel.Children.Add(nextButton);

            var pageInfo = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220)),
                Margin = new Thickness(0, 0, 16, 0)
            };
            pageInfo.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("CurrentPage") { StringFormat = "第 {0} 页 / " });
            pagePanel.Children.Add(pageInfo);

            var totalPageInfo = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220)),
                Margin = new Thickness(0, 0, 16, 0)
            };
            totalPageInfo.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("TotalPages") { StringFormat = "共 {0} 页" });
            pagePanel.Children.Add(totalPageInfo);

            DockPanel.SetDock(pagePanel, Dock.Right);
            dock.Children.Add(pagePanel);

            var statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 200, 220))
            };
            statusText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("StatusMessage"));
            dock.Children.Add(statusText);

            border.Child = dock;
            return border;
        }

        private Grid CreateCardContent(string title, TextBlock content)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 200, 255)),
                Margin = new Thickness(12, 10, 12, 8)
            };
            Grid.SetRow(titleBlock, 0);
            grid.Children.Add(titleBlock);

            Grid.SetRow(content, 1);
            grid.Children.Add(content);

            return grid;
        }

        private void OpenSelectedDetailFromGrid(DataGrid dataGrid)
        {
            if (dataGrid.SelectedItem is TreatmentRecord record)
            {
                _viewModel.SelectedRecord = record;
                _ = _viewModel.OpenSessionDetailAsync(record.SessionId);
                RefreshDetailOverlay();
            }
        }

        private sealed class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => value is Visibility v && v == Visibility.Visible;
        }

        private enum DetailGridKind
        {
            Pressure,
            Edema,
            Command
        }

        private void OnDataGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid dataGrid)
            {
                return;
            }

            var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row?.DataContext is TreatmentRecord record)
            {
                dataGrid.SelectedItem = record;
                _viewModel.SelectedRecord = record;
                _ = _viewModel.OpenSessionDetailAsync(record.SessionId);
                RefreshDetailOverlay();
            }
        }
    }
}
