using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Upcomputer.UI.ViewModels;

namespace Upcomputer.UI.Views
{
    public class UserManagementView : UserControl
    {
        private readonly UserManagementViewModel _viewModel;

        public UserManagementView(UserManagementViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;
            Content = BuildUserInterface();
        }

        private UIElement BuildUserInterface()
        {
            var root = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 25, 35)),
                Margin = new Thickness(15)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(CreateHeader());

            var listBorder = CreateList();
            Grid.SetRow(listBorder, 1);
            root.Children.Add(listBorder);

            var footer = CreateFooter();
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            return root;
        }

        private Border CreateHeader()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "用户管理",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
            searchRow.Children.Add(new TextBlock
            {
                Text = "搜索：",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var searchBox = new TextBox
            {
                Height = 32,
                Width = 360,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Foreground = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 85, 100))
            };
            searchBox.SetBinding(TextBox.TextProperty, new Binding(nameof(UserManagementViewModel.SearchText))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            var searchBtn = new Button
            {
                Content = "搜索",
                Height = 32,
                Width = 90,
                Margin = new Thickness(10, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White
            };
            searchBtn.SetBinding(Button.CommandProperty, new Binding(nameof(UserManagementViewModel.SearchCommand)));

            searchRow.Children.Add(searchBox);
            searchRow.Children.Add(searchBtn);
            stack.Children.Add(searchRow);

            Grid.SetColumn(stack, 0);
            grid.Children.Add(stack);

            var addBtn = new Button
            {
                Content = "增加用户",
                Height = 34,
                Width = 110,
                Margin = new Thickness(10, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            addBtn.SetBinding(Button.CommandProperty, new Binding(nameof(UserManagementViewModel.AddUserCommand)));
            Grid.SetColumn(addBtn, 1);
            grid.Children.Add(addBtn);

            var delBtn = new Button
            {
                Content = "删除用户",
                Height = 34,
                Width = 110,
                Background = new SolidColorBrush(Color.FromRgb(80, 90, 100)),
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            delBtn.SetBinding(Button.CommandProperty, new Binding(nameof(UserManagementViewModel.DeleteUserCommand)));
            Grid.SetColumn(delBtn, 2);
            grid.Children.Add(delBtn);

            border.Child = grid;
            return border;
        }

        private Border CreateList()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

            header.Children.Add(CreateHeaderText("用户名", 0));
            header.Children.Add(CreateHeaderText("性别", 1));
            header.Children.Add(CreateHeaderText("身高(cm)", 2));
            header.Children.Add(CreateHeaderText("体重(kg)", 3));
            header.Children.Add(CreateHeaderText("BMI", 4));

            grid.Children.Add(header);

            var list = new ListView
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 40, 50)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                SelectionMode = SelectionMode.Single
            };

            var itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            // Override the default ListViewItem template to avoid the built-in hover/selection chrome
            // that can create multi-colored separator artifacts inside GridView rows.
            var template = new ControlTemplate(typeof(ListViewItem));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "Bd";
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

            // Use GridViewRowPresenter so GridView columns render correctly (avoid showing UserProfile.ToString()).
            var rowPresenter = new FrameworkElementFactory(typeof(GridViewRowPresenter));
            rowPresenter.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            rowPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            rowPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            rowPresenter.SetValue(GridViewRowPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            rowPresenter.SetValue(GridViewRowPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            rowPresenter.SetBinding(
                GridViewRowPresenter.ColumnsProperty,
                new Binding("View.Columns")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListView), 1)
                });

            borderFactory.AppendChild(rowPresenter);
            template.VisualTree = borderFactory;
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, template));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(40, 55, 70))));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            itemStyle.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 120, 170))));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            itemStyle.Triggers.Add(selectedTrigger);

            var inactiveSelectedTrigger = new MultiTrigger();
            inactiveSelectedTrigger.Conditions.Add(new Condition { Property = ListViewItem.IsSelectedProperty, Value = true });
            inactiveSelectedTrigger.Conditions.Add(new Condition { Property = UIElement.IsKeyboardFocusWithinProperty, Value = false });
            inactiveSelectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 90, 130))));
            inactiveSelectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            itemStyle.Triggers.Add(inactiveSelectedTrigger);

            list.ItemContainerStyle = itemStyle;
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(UserManagementViewModel.Users)));
            list.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(UserManagementViewModel.SelectedUser))
            {
                Mode = BindingMode.TwoWay
            });

            var view = new GridView();
            view.Columns.Add(new GridViewColumn { Header = "用户名", DisplayMemberBinding = new Binding("UserName"), Width = 160 });
            view.Columns.Add(new GridViewColumn { Header = "性别", DisplayMemberBinding = new Binding("Gender"), Width = 80 });
            view.Columns.Add(new GridViewColumn { Header = "身高", DisplayMemberBinding = new Binding("HeightCm"), Width = 90 });
            view.Columns.Add(new GridViewColumn { Header = "体重", DisplayMemberBinding = new Binding("WeightKg"), Width = 90 });
            view.Columns.Add(new GridViewColumn { Header = "BMI", DisplayMemberBinding = new Binding("BmiDisplay"), Width = 70 });
            list.View = view;

            Grid.SetRow(list, 1);
            grid.Children.Add(list);

            border.Child = grid;
            return border;
        }

        private static TextBlock CreateHeaderText(string text, int column)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(tb, column);
            return tb;
        }

        private Border CreateFooter()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(40, 50, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var prevBtn = new Button
            {
                Content = "上一页",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(80, 90, 100)),
                Foreground = Brushes.White
            };
            prevBtn.SetBinding(Button.CommandProperty, new Binding(nameof(UserManagementViewModel.PreviousPageCommand)));
            Grid.SetColumn(prevBtn, 0);
            grid.Children.Add(prevBtn);

            var status = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            status.SetBinding(TextBlock.TextProperty, new Binding(nameof(UserManagementViewModel.StatusText)));
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);

            var nextBtn = new Button
            {
                Content = "下一页",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(80, 90, 100)),
                Foreground = Brushes.White
            };
            nextBtn.SetBinding(Button.CommandProperty, new Binding(nameof(UserManagementViewModel.NextPageCommand)));
            Grid.SetColumn(nextBtn, 2);
            grid.Children.Add(nextBtn);

            border.Child = grid;
            return border;
        }
    }
}
