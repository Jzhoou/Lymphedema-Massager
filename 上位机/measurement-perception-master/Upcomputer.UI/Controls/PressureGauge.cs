using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Upcomputer.UI.Controls
{
    public class PressureGauge : Control
    {
        static PressureGauge()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(PressureGauge),
                new FrameworkPropertyMetadata(typeof(PressureGauge)));
        }


        private Arc? _backgroundArc;
        private Arc? _valueArc;
        private TextBlock? _valueText;
        private TextBlock? _titleText;
        // 仪表盘固定使用 270° 标准圆弧（左下到右下）
        private const double StartAngle = 135;
        private const double TotalArcAngle = 270;

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(PressureGauge),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(PressureGauge),
                new PropertyMetadata(100.0, OnRangeChanged));

        public static readonly DependencyProperty ThresholdProperty =
            DependencyProperty.Register(nameof(Threshold), typeof(double), typeof(PressureGauge),
                new PropertyMetadata(80.0, OnRangeChanged));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(PressureGauge),
                new PropertyMetadata("压力"));

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(PressureGauge),
                new PropertyMetadata("g"));

        public static readonly DependencyProperty IsAlertProperty =
            DependencyProperty.Register(nameof(IsAlert), typeof(bool), typeof(PressureGauge),
                new PropertyMetadata(false));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        public double Threshold
        {
            get => (double)GetValue(ThresholdProperty);
            set => SetValue(ThresholdProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public bool IsAlert
        {
            get => (bool)GetValue(IsAlertProperty);
            set => SetValue(IsAlertProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var gauge = (PressureGauge)d;
            gauge.IsAlert = gauge.Value > gauge.Threshold;
            gauge.UpdateVisual();
        }

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var gauge = (PressureGauge)d;
            gauge.IsAlert = gauge.Value > gauge.Threshold;
            gauge.UpdateVisual();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _backgroundArc = GetTemplateChild("PART_BackgroundArc") as Arc;
            _valueArc = GetTemplateChild("PART_ValueArc") as Arc;
            _valueText = GetTemplateChild("PART_ValueText") as TextBlock;
            _titleText = GetTemplateChild("PART_TitleText") as TextBlock;

            // 初始化背景圆弧（固定270°）
            if (_backgroundArc != null)
            {
                _backgroundArc.StartAngle = StartAngle;
                _backgroundArc.EndAngle = StartAngle + TotalArcAngle;
            }

            // 初始化数值圆弧起始角度
            if (_valueArc != null)
            {
                _valueArc.StartAngle = StartAngle;
            }

            UpdateVisual();
        }

        private void UpdateVisual()
        {
            if (_valueArc == null || _valueText == null) return;

            // 计算百分比（0~1）
            double percentage = MaxValue <= 0 ? 0 : Math.Clamp(Value / MaxValue, 0, 1);

            // 计算圆弧结束角度（标准270°仪表盘）
            var targetEndAngle = StartAngle + percentage * TotalArcAngle;
            var currentEndAngle = _valueArc.EndAngle;

            if (Math.Abs(currentEndAngle - targetEndAngle) < 0.01)
            {
                _valueArc.EndAngle = targetEndAngle;
            }
            else
            {
                var animation = new DoubleAnimation
                {
                    To = targetEndAngle,
                    Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                _valueArc.BeginAnimation(Arc.EndAngleProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }

            // 报警颜色
            _valueArc.Stroke = IsAlert ? Brushes.Red : new SolidColorBrush(Color.FromRgb(52, 152, 219));
            _valueText.Text = $"{Value:F1}";
            _valueText.Foreground = IsAlert ? Brushes.Red : Brushes.White;

            if (_titleText != null)
                _titleText.Text = Title;
        }



    }

 
}
