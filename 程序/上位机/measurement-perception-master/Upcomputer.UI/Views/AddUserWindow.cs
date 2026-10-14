using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Upcomputer.UI.ViewModels;

namespace Upcomputer.UI.Views
{
    public class AddUserWindow : Window
    {
        private readonly AddUserDialogViewModel _viewModel;

        public AddUserWindow(AddUserDialogViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;

            Title = "新增用户";
            Width = 420;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(15, 25, 35));

            Content = BuildContent();
        }

        private UIElement BuildContent()
        {
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var formBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 30, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12)
            };

            var form = new StackPanel();
            form.Children.Add(CreateLabeledTextBox("用户名", v => _viewModel.UserName = v, () => _viewModel.UserName));

            form.Children.Add(CreateLabeledComboBox("性别", new[] { "男", "女", "未知" }, v => _viewModel.Gender = v, () => _viewModel.Gender));

            form.Children.Add(CreateLabeledDatePicker("出生日期", v => _viewModel.BirthDate = v, () => _viewModel.BirthDate));

            form.Children.Add(CreateLabeledTextBox("手机号", v => _viewModel.PhoneNumber = v, () => _viewModel.PhoneNumber ?? string.Empty));

            form.Children.Add(CreateLabeledTextBox("身高(cm)", v => _viewModel.HeightText = v, () => _viewModel.HeightText));

            form.Children.Add(CreateLabeledTextBox("体重(kg)", v => _viewModel.WeightText = v, () => _viewModel.WeightText));

            var bmiRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            bmiRow.Children.Add(new TextBlock
            {
                Text = "BMI：",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                Width = 90,
                VerticalAlignment = VerticalAlignment.Center
            });
            var bmiText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            bmiText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(AddUserDialogViewModel.BmiDisplay)));
            bmiRow.Children.Add(bmiText);
            form.Children.Add(bmiRow);

            formBorder.Child = form;
            root.Children.Add(formBorder);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var okBtn = new Button
            {
                Content = "确定",
                Width = 100,
                Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(0, 150, 200)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 10, 0)
            };
            okBtn.Click += (s, e) => OnOk();

            var cancelBtn = new Button
            {
                Content = "取消",
                Width = 100,
                Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(80, 90, 100)),
                Foreground = Brushes.White
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };

            btnRow.Children.Add(okBtn);
            btnRow.Children.Add(cancelBtn);

            Grid.SetRow(btnRow, 1);
            root.Children.Add(btnRow);

            return root;
        }

        private void OnOk()
        {
            if (!_viewModel.Validate(out var error))
            {
                MessageBox.Show(error, "输入不合规", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private static FrameworkElement CreateLabeledTextBox(string label, Action<string> setter, Func<string> getter)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var tb = new TextBox
            {
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Foreground = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 85, 100))
            };

            tb.Text = getter();
            tb.TextChanged += (s, e) => setter(tb.Text);

            panel.Children.Add(tb);
            return panel;
        }

        private static FrameworkElement CreateLabeledComboBox(string label, string[] items, Action<string?> setter, Func<string?> getter)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var cb = new ComboBox
            {
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Foreground = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 85, 100)),
                ItemsSource = items
            };

            cb.SelectedItem = getter();
            cb.SelectionChanged += (s, e) => setter(cb.SelectedItem as string);

            panel.Children.Add(cb);
            return panel;
        }

        private static FrameworkElement CreateLabeledDatePicker(string label, Action<DateTime?> setter, Func<DateTime?> getter)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 220)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var dp = new DatePicker
            {
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Foreground = Brushes.Black
            };

            dp.SelectedDate = getter();
            dp.SelectedDateChanged += (s, e) => setter(dp.SelectedDate);

            panel.Children.Add(dp);
            return panel;
        }
    }
}
