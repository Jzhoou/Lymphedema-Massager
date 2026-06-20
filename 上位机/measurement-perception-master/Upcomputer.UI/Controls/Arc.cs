using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Upcomputer.UI.Controls
{
    public class Arc : Shape
    {
        public double StartAngle
        {
            get => (double)GetValue(StartAngleProperty);
            set => SetValue(StartAngleProperty, value);
        }

        public static readonly DependencyProperty StartAngleProperty =
            DependencyProperty.Register(nameof(StartAngle), typeof(double), typeof(Arc),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double EndAngle
        {
            get => (double)GetValue(EndAngleProperty);
            set => SetValue(EndAngleProperty, value);
        }

        public static readonly DependencyProperty EndAngleProperty =
            DependencyProperty.Register(nameof(EndAngle), typeof(double), typeof(Arc),
                new FrameworkPropertyMetadata(90.0, FrameworkPropertyMetadataOptions.AffectsRender));

        protected override Geometry DefiningGeometry
        {
            get
            {
                // 检查 ActualWidth 和 ActualHeight 是否有效
                if (ActualWidth <= 0 || ActualHeight <= 0 || double.IsNaN(ActualWidth) || double.IsNaN(ActualHeight))
                    return Geometry.Empty;

                double strokeThickness = StrokeThickness;
                if (double.IsNaN(strokeThickness))
                    strokeThickness = 0;

                // 计算可用半径
                double radiusX = (ActualWidth - strokeThickness) / 2;
                double radiusY = (ActualHeight - strokeThickness) / 2;

                // 确保半径为正数
                if (radiusX <= 0 || radiusY <= 0)
                    return Geometry.Empty;

                // 使用统一半径（取最小值保证圆弧不会超出边界）
                double radius = Math.Min(radiusX, radiusY);
                if (radius <= 0)
                    return Geometry.Empty;

                // 中心点
                double centerX = ActualWidth / 2;
                double centerY = ActualHeight / 2;

                // 角度转换
                double startRad = StartAngle * Math.PI / 180;
                double endRad = EndAngle * Math.PI / 180;

                // 计算起点和终点
                Point startPoint = new Point(
                    centerX + radius * Math.Cos(startRad),
                    centerY + radius * Math.Sin(startRad));

                Point endPoint = new Point(
                    centerX + radius * Math.Cos(endRad),
                    centerY + radius * Math.Sin(endRad));

                // 判断是否为大弧
                bool isLargeArc = Math.Abs(EndAngle - StartAngle) > 180;

                // 创建几何图形
                StreamGeometry geometry = new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    ctx.BeginFigure(startPoint, false, false);
                    ctx.ArcTo(endPoint, new Size(radius, radius), 0, isLargeArc,
                        SweepDirection.Clockwise, true, false);
                }

                return geometry;
            }
        }
    }
}