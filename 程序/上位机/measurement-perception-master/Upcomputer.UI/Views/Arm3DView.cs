using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;
using System.Collections.Generic;
using System.Linq;
using Upcomputer.Common.Helpers;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;
using Upcomputer.Communication.Protocol;

namespace Upcomputer.UI.Views
{
    public class Arm3DView : UserControl
    {
        // 张大头步进电机参数（16384线磁编码器，丝杆导程8mm，行程325mm）
        private const int ENCODER_PULSES_PER_REV = 16384;
        private const double LEAD_SCREW_PITCH_MM = 8.0;
        private const double MAX_TRAVEL_MM = 325.0;

        // 电机2编码器标定点：0=手腕端，-8465945=回退起点（上臂侧）
        private const int MOTOR2_ENCODER_WRIST = 0;
        private const int MOTOR2_ENCODER_BACK_START_ESTIMATE = -8465945;
        private const double PATH_START_OFFSET = 0.3;

        private const double MotorMin = 0.0;
        private const double MotorMax = MAX_TRAVEL_MM;
        private double _modelXMin = -100;
        private double _modelXMax = 150.0;

        private readonly HelixViewport3D _viewport;
        private readonly ModelVisual3D _armModel = new ModelVisual3D();
        private readonly TubeVisual3D _mainRing;
        private readonly TubeVisual3D _outerHalo;
        private readonly SphereVisual3D _centerSphere;

        // 用 Transform3DGroup 替代单一的 TranslateTransform，支持旋转
        private readonly RotateTransform3D _indicatorRotate = new RotateTransform3D();
        private readonly TranslateTransform3D _indicatorTranslate = new TranslateTransform3D();
        private readonly Transform3DGroup _indicatorTransformGroup = new Transform3DGroup();

        private readonly TextBlock _statusText;
        private readonly TextBlock _regionText;

        private readonly ICommunicationService? _communicationService;
        private int _motor2DynamicMin = MOTOR2_ENCODER_BACK_START_ESTIMATE;

        private double _modelBoundsMinX;
        private double _modelBoundsMaxX;
        private double _modelBoundsMinY;
        private double _modelBoundsMaxY;
        private double _modelBoundsMinZ;
        private double _modelBoundsMaxZ;

        private List<Point3D> _armCenterPath = new List<Point3D>();
        private double _pathLength;

        // ==================== 速度预测 + 位置校正 平滑插值 ====================
        private readonly DispatcherTimer _interpolationTimer;
        private double _displayedPosition;   // 当前显示位置（mm）
        private double _targetPosition;      // 下位机目标位置（mm）
        private double _lastMeasuredPosition;
        private bool _hasMeasuredPosition;
        private double _estimatedVelocity;   // 估算速度（mm/ms）
        private DateTime _lastPositionTime;
        private DateTime _lastInterpolationTickTime;
        private bool _isMoving;
        private const double VELOCITY_STOP_TOLERANCE = 0.01; // mm/ms，低于此值可停止插值
        private const double POSITION_STOP_TOLERANCE_MM = 0.10; // mm，低于此值视为到位
        private const double STALE_TIMEOUT_MS = 500.0;   // 超过此时间无新数据则速度衰减
        private const double VELOCITY_EMA_ALPHA = 0.3;   // 速度EMA平滑系数
        private const double MIN_STEP_MM = 0.05; // 最小推进步长，避免速度很小时停滞
        private readonly double _positionCorrectionGain;
        private readonly double _maxStepMm;
        private readonly double _hardSyncThresholdMm;

        public Arm3DView() : this(null) { }

        public Arm3DView(ICommunicationService? communicationService)
        {
            _communicationService = communicationService;

            var appConfig = AppConfigHelper.Load();
            _positionCorrectionGain = Math.Clamp(appConfig.Arm3DCorrectionGain, 0.0, 1.0);
            _maxStepMm = Math.Clamp(appConfig.Arm3DMaxStepMm, 0.5, 50.0);
            _hardSyncThresholdMm = Math.Clamp(appConfig.Arm3DHardSyncThresholdMm, 1.0, MAX_TRAVEL_MM);

            // 初始化变换组：先旋转（朝向切线），再平移
            _indicatorTransformGroup.Children.Add(_indicatorRotate);
            _indicatorTransformGroup.Children.Add(_indicatorTranslate);

            // 初始化插值定时器（≈30fps）
            _interpolationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _interpolationTimer.Tick += OnInterpolationTick;

            // Main layout: 3D viewport + right panel
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

            // Helix viewport — 禁用所有鼠标交互
            _viewport = new HelixViewport3D
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                ShowCoordinateSystem = true,
                IsHeadLightEnabled = true,
                IsRotationEnabled = false,
                IsPanEnabled = false,
                IsZoomEnabled = false,
                ShowViewCube = false,
                ZoomExtentsWhenLoaded = true
            };

            _viewport.Margin = new Thickness(8);

            // Camera
            _viewport.Camera = new PerspectiveCamera(
                new Point3D(-87, 55.5, -458.8),
                new Vector3D(85.719, -49.325, 419.687),
                new Vector3D(-0.176, 0.826, 0.536),
                45);

            // Lights
            _viewport.Children.Add(new DefaultLights());

            // Arm model container
            _viewport.Children.Add(_armModel);

            // Create indicator: three layers
            _mainRing = CreateRing(40 / 2.0, 6, 64);
            _mainRing.Fill = new SolidColorBrush(Colors.DodgerBlue);

            _outerHalo = CreateRing(56 / 2.0, 10, 64);
            _outerHalo.Fill = new SolidColorBrush(Color.FromArgb(120, 0, 200, 255));

            _centerSphere = new SphereVisual3D
            {
                Radius = 6,
                Fill = Brushes.White
            };

            // Apply transform group to indicator parts
            _mainRing.Transform = _indicatorTransformGroup;
            _outerHalo.Transform = _indicatorTransformGroup;
            _centerSphere.Transform = _indicatorTransformGroup;

            _viewport.Children.Add(_outerHalo);
            _viewport.Children.Add(_mainRing);
            _viewport.Children.Add(_centerSphere);

            Grid.SetColumn(_viewport, 0);
            root.Children.Add(_viewport);

            // Right side controls（仅保留位置、区域信息）
            var panel = new StackPanel { Margin = new Thickness(8) };

            _statusText = new TextBlock
            {
                Text = "",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 4, 0, 4)
            };
            panel.Children.Add(_statusText);

            _regionText = new TextBlock
            {
                Text = "区域: -",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 4, 0, 8)
            };
            panel.Children.Add(_regionText);

            // 如果有通信服务，就订阅下位机上报的电机位置数据
            if (_communicationService != null)
            {
                _communicationService.MotorPositionReceived += OnMotorPositionReceived;
            }

            // Add panel to grid
            Grid.SetColumn(panel, 1);
            root.Children.Add(panel);

            Content = root;

            // Attempt to load model
            LoadArmModel();

            // 加载/卸载时正确管理事件订阅
            Loaded += (_, _) =>
            {
                if (_communicationService != null)
                {
                    // 防止重复订阅
                    _communicationService.MotorPositionReceived -= OnMotorPositionReceived;
                    _communicationService.MotorPositionReceived += OnMotorPositionReceived;
                }
            };
            Unloaded += (_, _) =>
            {
                if (_communicationService != null)
                {
                    _communicationService.MotorPositionReceived -= OnMotorPositionReceived;
                }
                StopInterpolation();
            };
        }

        /// <summary>
        /// 收到下位机真实电机位置时更新：更新目标 + 速度估算 + 平滑追目标
        /// </summary>
        private void OnMotorPositionReceived(object? sender, MotorPositionResponse response)
        {
            // 最小值使用动态学习，不固定死在某个数；0 仍作为手腕参考点
            if (response.Motor2EncoderValue < _motor2DynamicMin)
            {
                _motor2DynamicMin = response.Motor2EncoderValue;
            }

            int span = MOTOR2_ENCODER_WRIST - _motor2DynamicMin;
            if (span <= 0)
            {
                span = 1;
            }

            double normalized = 1.0 - (response.Motor2EncoderValue - _motor2DynamicMin) / (double)span;
            normalized = Math.Clamp(normalized, 0.0, 1.0);
            double realPosition = normalized * MAX_TRAVEL_MM;

            // 计算速度（mm/ms）
            DateTime now = DateTime.UtcNow;
            double dtMs = (now - _lastPositionTime).TotalMilliseconds;

            if (_lastPositionTime != default && _hasMeasuredPosition && dtMs > 0 && dtMs < STALE_TIMEOUT_MS)
            {
                double rawVelocity = (realPosition - _lastMeasuredPosition) / dtMs;
                // 指数移动平均平滑
                _estimatedVelocity = VELOCITY_EMA_ALPHA * rawVelocity + (1 - VELOCITY_EMA_ALPHA) * _estimatedVelocity;
            }
            else if (_lastPositionTime == default)
            {
                // 首次收到数据，直接定位
                _displayedPosition = realPosition;
                UpdateIndicatorPosition(realPosition);
            }

            if (Math.Abs(realPosition - _displayedPosition) >= _hardSyncThresholdMm)
            {
                // 预测与真实偏差过大时，优先对齐真实位置，避免提前到终点/起点
                _displayedPosition = realPosition;
                UpdateIndicatorPosition(_displayedPosition);
            }

            _lastPositionTime = now;
            _lastMeasuredPosition = realPosition;
            _hasMeasuredPosition = true;
            _targetPosition = realPosition;

            // 启动追目标插值
            StartInterpolation();
        }

        private void StartInterpolation()
        {
            if (!_isMoving)
            {
                _isMoving = true;
                _lastInterpolationTickTime = DateTime.UtcNow;
                _interpolationTimer.Start();
            }
        }

        private void StopInterpolation()
        {
            _isMoving = false;
            _interpolationTimer.Stop();
        }

        /// <summary>
        /// 插值定时器：每33ms平滑追向目标位置
        /// </summary>
        private void OnInterpolationTick(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            var dtMs = (now - _lastInterpolationTickTime).TotalMilliseconds;
            if (dtMs <= 0)
            {
                dtMs = _interpolationTimer.Interval.TotalMilliseconds;
            }

            dtMs = Math.Clamp(dtMs, 5.0, 100.0);
            _lastInterpolationTickTime = now;
            double dtSinceLastUpdate = (DateTime.UtcNow - _lastPositionTime).TotalMilliseconds;

            // 如果长时间没收到新数据，速度衰减到0
            if (dtSinceLastUpdate > STALE_TIMEOUT_MS)
            {
                _estimatedVelocity *= 0.8;
                if (Math.Abs(_estimatedVelocity) < VELOCITY_STOP_TOLERANCE)
                {
                    StopInterpolation();
                    return;
                }
            }

            // 计算剩余距离
            double remaining = _targetPosition - _displayedPosition;

            // 如果已到达目标附近，停止插值
            if (Math.Abs(remaining) < POSITION_STOP_TOLERANCE_MM)
            {
                _displayedPosition = _targetPosition;
                StopInterpolation();
                UpdateIndicatorPosition(_displayedPosition);
                return;
            }

            // 速度预测 + 位置纠偏，既保证连续性也避免卡顿
            double predictedStep = _estimatedVelocity * dtMs;
            double correctionStep = remaining * _positionCorrectionGain;
            double step = predictedStep + correctionStep;

            // 若预测方向与目标相反，优先采用纠偏项
            if (Math.Sign(step) != Math.Sign(remaining))
            {
                step = correctionStep;
            }

            // 防止速度过小时出现停滞
            if (Math.Abs(step) < MIN_STEP_MM)
            {
                step = Math.Sign(remaining) * Math.Min(MIN_STEP_MM, Math.Abs(remaining));
            }

            // 限制单帧位移，避免瞬移
            step = Math.Clamp(step, -_maxStepMm, _maxStepMm);

            if (Math.Abs(step) > Math.Abs(remaining))
            {
                step = remaining; // 精确停到目标
            }

            _displayedPosition += step;

            // 钳位到有效范围
            _displayedPosition = Math.Clamp(_displayedPosition, 0.0, MAX_TRAVEL_MM);

            UpdateIndicatorPosition(_displayedPosition);
        }

        /// <summary>
        /// 仅更新圆环位置和区域标签（不依赖数据源）
        /// </summary>
        private void UpdateIndicatorPosition(double positionMm)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                    new Action(() => UpdateIndicatorPosition(positionMm)));
                return;
            }

            var pathPoint = GetPointOnPath(positionMm);
            var tangent = GetPathTangent(pathPoint);

            _indicatorTranslate.OffsetX = pathPoint.X;
            _indicatorTranslate.OffsetY = pathPoint.Y;
            _indicatorTranslate.OffsetZ = pathPoint.Z;

            SetIndicatorRotation(tangent);

            _statusText.Text = $"位置: {positionMm:F1}";
            UpdateIndicatorByRegion(pathPoint.X);
        }

        private void MakeModelDoubleSided(Model3D model)
        {
            if (model is GeometryModel3D gm)
            {
                if (gm.Material != null && gm.BackMaterial == null)
                {
                    gm.BackMaterial = gm.Material.Clone();
                }
            }
            else if (model is Model3DGroup g)
            {
                foreach (var child in g.Children)
                {
                    MakeModelDoubleSided(child);
                }
            }
        }

        private static TubeVisual3D CreateRing(double radius, double thickness, int thetaDiv)
        {
            var points = new Point3DCollection();
            for (int i = 0; i <= thetaDiv; i++)
            {
                var theta = 2.0 * Math.PI * i / thetaDiv;
                points.Add(new Point3D(0, radius * Math.Cos(theta), radius * Math.Sin(theta)));
            }

            return new TubeVisual3D
            {
                Path = points,
                Diameter = thickness,
                IsPathClosed = true
            };
        }

        // ==================== 核心：计算路径切线方向 ====================
        private Vector3D GetPathTangent(Point3D point)
        {
            if (_armCenterPath.Count < 2)
                return new Vector3D(1, 0, 0);

            int closestSegment = 0;
            double minDist = double.MaxValue;

            for (int i = 0; i < _armCenterPath.Count - 1; i++)
            {
                var dist = PointToSegmentDistance(point, _armCenterPath[i], _armCenterPath[i + 1]);
                if (dist < minDist)
                {
                    minDist = dist;
                    closestSegment = i;
                }
            }

            var tangent = _armCenterPath[closestSegment + 1] - _armCenterPath[closestSegment];
            tangent.Normalize();
            return tangent;
        }

        private double PointToSegmentDistance(Point3D point, Point3D segA, Point3D segB)
        {
            var ab = segB - segA;
            var ap = point - segA;
            var t = Vector3D.DotProduct(ap, ab) / Vector3D.DotProduct(ab, ab);
            t = Math.Max(0, Math.Min(1, t));

            var closest = segA + ab * t;
            return (point - closest).Length;
        }

        // ==================== 计算中心路径 ====================
        private void CalculateArmCenterPath(ModelVisual3D modelVisual)
        {
            _armCenterPath.Clear();

            if (modelVisual?.Content == null) return;

            var allVertices = new List<Point3D>();
            ExtractVerticesRecursive(modelVisual.Content, modelVisual.Transform ?? Transform3D.Identity, allVertices);

            if (allVertices.Count < 100)
            {
                _armCenterPath.Add(new Point3D(_modelBoundsMinX, 0, 0));
                _armCenterPath.Add(new Point3D(_modelBoundsMaxX, 0, 0));
                _pathLength = _modelBoundsMaxX - _modelBoundsMinX;
                _armCenterPath.Reverse();
                return;
            }

            var segments = 100;
            var segmentWidth = (_modelXMax - _modelXMin) / segments;

            for (int i = 0; i <= segments; i++)
            {
                var xMin = _modelXMin + i * segmentWidth;
                var xMax = xMin + segmentWidth;

                var segmentVertices = allVertices
                    .Where(v => v.X >= xMin && v.X < xMax)
                    .ToList();

                if (segmentVertices.Count > 0)
                {
                    var avgY = segmentVertices.Average(v => v.Y);
                    var avgZ = segmentVertices.Average(v => v.Z);
                    var centerX = xMin + segmentWidth / 2;

                    _armCenterPath.Add(new Point3D(centerX, avgY, avgZ));
                }
                else
                {
                    var centerX = xMin + segmentWidth / 2;
                    if (_armCenterPath.Count > 0)
                    {
                        var last = _armCenterPath[_armCenterPath.Count - 1];
                        _armCenterPath.Add(new Point3D(centerX, last.Y, last.Z));
                    }
                    else
                    {
                        _armCenterPath.Add(new Point3D(centerX, 0, 0));
                    }
                }
            }

            SmoothPath();

            _armCenterPath.Reverse();

            _pathLength = 0;
            for (int i = 1; i < _armCenterPath.Count; i++)
            {
                _pathLength += (_armCenterPath[i] - _armCenterPath[i - 1]).Length;
            }
        }

        private void ExtractVerticesRecursive(Model3D model, Transform3D parentTransform, List<Point3D> vertices)
        {
            if (model is GeometryModel3D gm && gm.Geometry is MeshGeometry3D mesh)
            {
                var transform = parentTransform;
                if (gm.Transform != null && gm.Transform != Transform3D.Identity)
                {
                    var tg = new Transform3DGroup();
                    tg.Children.Add(parentTransform);
                    tg.Children.Add(gm.Transform);
                    transform = tg;
                }

                foreach (var pos in mesh.Positions)
                {
                    vertices.Add(transform.Transform(pos));
                }
            }
            else if (model is Model3DGroup group)
            {
                var transform = parentTransform;
                if (group.Transform != null && group.Transform != Transform3D.Identity)
                {
                    var tg = new Transform3DGroup();
                    tg.Children.Add(parentTransform);
                    tg.Children.Add(group.Transform);
                    transform = tg;
                }

                foreach (var child in group.Children)
                {
                    ExtractVerticesRecursive(child, transform, vertices);
                }
            }
        }

        private void SmoothPath()
        {
            if (_armCenterPath.Count < 3) return;

            for (int pass = 0; pass < 5; pass++)
            {
                var smoothed = new List<Point3D> { _armCenterPath[0] };

                for (int i = 1; i < _armCenterPath.Count - 1; i++)
                {
                    var prev = _armCenterPath[i - 1];
                    var current = _armCenterPath[i];
                    var next = _armCenterPath[i + 1];

                    var smoothedPoint = new Point3D(
                        current.X,
                        (prev.Y + current.Y * 2 + next.Y) / 4,
                        (prev.Z + current.Z * 2 + next.Z) / 4
                    );
                    smoothed.Add(smoothedPoint);
                }

                smoothed.Add(_armCenterPath[_armCenterPath.Count - 1]);
                _armCenterPath = smoothed;
            }
        }

        private Visual3D CreateSimpleProceduralArmVisual()
        {
            var container = new ModelVisual3D();

            var matBrush = new SolidColorBrush(Color.FromRgb(200, 180, 160));
            var jointBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150));

            var upperPath = new Point3DCollection
            {
                new Point3D(-80, 0, 0),
                new Point3D(-20, 0, 0)
            };
            var upper = new TubeVisual3D
            {
                Path = upperPath,
                Diameter = 24,
                IsPathClosed = false,
                Fill = matBrush
            };
            container.Children.Add(upper);

            var elbow = new SphereVisual3D
            {
                Center = new Point3D(-20, 0, 0),
                Radius = 8,
                Fill = jointBrush
            };
            container.Children.Add(elbow);

            var forePath = new Point3DCollection
            {
                new Point3D(-20, 0, 0),
                new Point3D(60, 0, 0)
            };
            var fore = new TubeVisual3D
            {
                Path = forePath,
                Diameter = 20,
                IsPathClosed = false,
                Fill = matBrush
            };
            container.Children.Add(fore);

            var wrist = new SphereVisual3D
            {
                Center = new Point3D(60, 0, 0),
                Radius = 7,
                Fill = jointBrush
            };
            container.Children.Add(wrist);

            var hand = new BoxVisual3D
            {
                Center = new Point3D(100, -5, 0),
                Width = 40,
                Height = 10,
                Length = 20,
                Fill = matBrush
            };
            container.Children.Add(hand);

            return container;
        }

        private void ApplyProceduralFallback(string statusMessage)
        {
            var visual = CreateSimpleProceduralArmVisual();
            _viewport.Children.Add(visual);

            _modelBoundsMinX = -90;
            _modelBoundsMaxX = 120;
            _modelBoundsMinY = -30;
            _modelBoundsMaxY = 30;
            _modelBoundsMinZ = -30;
            _modelBoundsMaxZ = 30;

            var margin = Math.Max(10.0, (_modelBoundsMaxX - _modelBoundsMinX) * 0.05);
            _modelXMin = _modelBoundsMinX - margin;
            _modelXMax = _modelBoundsMaxX + margin;

            _armCenterPath.Clear();
            for (double x = _modelXMin; x <= _modelXMax; x += 5)
            {
                _armCenterPath.Add(new Point3D(x, 0, 0));
            }
            _pathLength = _modelXMax - _modelXMin;
            _armCenterPath.Reverse();

            var initPoint = GetPointOnPath(0);
            _indicatorTranslate.OffsetX = initPoint.X;
            _indicatorTranslate.OffsetY = initPoint.Y;
            _indicatorTranslate.OffsetZ = initPoint.Z;

            _viewport.ZoomExtents();
        }

        private void LoadArmModel()
        {
            try
            {
                var filePath = ResolveStlModelPath();
                if (filePath == null)
                {
                    _statusText.Text = BuildMissingModelMessage();
                    ApplyProceduralFallback("未找到STL模型，使用程序化手臂");
                    return;
                }

                var importer = new ModelImporter();
                importer.DefaultMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));

                var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(200, 180, 160)));

                var model = importer.Load(filePath);
                if (model == null)
                {
                    _statusText.Text = "STL模型加载失败，使用程序化手臂";
                    ApplyProceduralFallback("STL模型加载失败，使用程序化模型");
                    return;
                }

                ApplyMaterialToModel(model, material);

                var bounds = model.Bounds;

                if (bounds.IsEmpty ||
                    (bounds.SizeX < 0.001 && bounds.SizeY < 0.001 && bounds.SizeZ < 0.001))
                {
                    _viewport.Children.Remove(_armModel);
                    ApplyProceduralFallback("STL模型边界无效，使用程序化模型");
                    return;
                }

                var center = new Point3D(
                    bounds.X + bounds.SizeX / 2.0,
                    bounds.Y + bounds.SizeY / 2.0,
                    bounds.Z + bounds.SizeZ / 2.0);

                var maxDim = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
                double scale = 1.0;
                if (maxDim > 0)
                {
                    var desired = 300.0;
                    scale = desired / maxDim;
                    scale = Math.Max(0.01, Math.Min(10.0, scale));
                }

                var transformGroup = new Transform3DGroup();
                transformGroup.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
                transformGroup.Children.Add(new ScaleTransform3D(scale, scale, scale));
                transformGroup.Children.Add(new TranslateTransform3D(0, 5, 0));
                transformGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -90)));
                transformGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0)));
                transformGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0)));
                transformGroup.Children.Add(new TranslateTransform3D(0, 0, 0));

                _armModel.Content = model;
                _armModel.Transform = transformGroup;

                _modelBoundsMinX = (bounds.X - center.X) * scale;
                _modelBoundsMaxX = (bounds.X + bounds.SizeX - center.X) * scale;
                _modelBoundsMinY = (bounds.Y - center.Y) * scale;
                _modelBoundsMaxY = (bounds.Y + bounds.SizeY - center.Y) * scale;
                _modelBoundsMinZ = (bounds.Z - center.Z) * scale;
                _modelBoundsMaxZ = (bounds.Z + bounds.SizeZ - center.Z) * scale;

                CalculateArmCenterPath(_armModel);

                if (_modelBoundsMaxX > _modelBoundsMinX)
                {
                    var margin = Math.Max(10.0, (_modelBoundsMaxX - _modelBoundsMinX) * 0.05);
                    var extraStart = 50.0;
                    var extraEnd = 0.0;
                    _modelXMin = _modelBoundsMinX - margin - extraStart;
                    _modelXMax = _modelBoundsMaxX + margin + extraEnd;
                }

                var initPoint = GetPointOnPath(0);
                _indicatorTranslate.OffsetX = initPoint.X;
                _indicatorTranslate.OffsetY = initPoint.Y;
                _indicatorTranslate.OffsetZ = initPoint.Z;

                var tangent = GetPathTangent(initPoint);
                SetIndicatorRotation(tangent);

                MakeModelDoubleSided(model);

                _viewport.ZoomExtents();
            }
            catch (Exception ex)
            {
                _statusText.Text = $"加载STL模型失败: {ex.Message}";
                try
                {
                    ApplyProceduralFallback("使用程序化手臂模型");
                }
                catch { }
            }
        }

        private void SetIndicatorRotation(Vector3D tangent)
        {
            if (tangent.Length < 1e-6)
            {
                _indicatorRotate.Rotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
                return;
            }

            var xAxis = new Vector3D(1, 0, 0);

            var rotationAxis = Vector3D.CrossProduct(xAxis, tangent);
            var angle = Math.Acos(Vector3D.DotProduct(xAxis, tangent) / (tangent.Length));

            var angleDeg = angle * 180.0 / Math.PI;

            if (rotationAxis.Length < 1e-6)
            {
                _indicatorRotate.Rotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
            }
            else
            {
                rotationAxis.Normalize();
                _indicatorRotate.Rotation = new AxisAngleRotation3D(rotationAxis, angleDeg);
            }
        }

        private void ApplyMaterialToModel(Model3D model, Material material)
        {
            if (model is GeometryModel3D gm)
            {
                gm.Material = material;
                gm.BackMaterial = material;
            }
            else if (model is Model3DGroup group)
            {
                foreach (var child in group.Children)
                {
                    ApplyMaterialToModel(child, material);
                }
            }
        }

        private void CountGeometry(Model3D model, ref int geomCount, ref int triCount)
        {
            if (model is GeometryModel3D gm && gm.Geometry is MeshGeometry3D mesh)
            {
                geomCount++;
                if (mesh.TriangleIndices != null && mesh.TriangleIndices.Count > 0)
                {
                    triCount += mesh.TriangleIndices.Count / 3;
                }
            }
            else if (model is Model3DGroup group)
            {
                foreach (var child in group.Children)
                {
                    CountGeometry(child, ref geomCount, ref triCount);
                }
            }
        }

        private string? ResolveStlModelPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var searchDirs = new List<string>
            {
                Path.Combine(baseDir, "Resources"),
                Path.Combine(Environment.CurrentDirectory, "Resources"),
                Path.Combine(AppContext.BaseDirectory, "Resources")
            };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir))
                    continue;

                var armStl = Path.Combine(dir, "Arm.stl");
                if (File.Exists(armStl))
                    return armStl;

                var stlFiles = Directory.GetFiles(dir, "*.stl");
                if (stlFiles.Length > 0)
                {
                    Array.Sort(stlFiles, StringComparer.OrdinalIgnoreCase);
                    return stlFiles[0];
                }
            }

            return null;
        }

        private string BuildMissingModelMessage()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Resources"),
                Path.Combine(Environment.CurrentDirectory, "Resources")
            };

            foreach (var dir in candidates)
            {
                if (!Directory.Exists(dir))
                    continue;

                var objFiles = Directory.GetFiles(dir, "*.obj").Length;
                var blendFiles = Directory.GetFiles(dir, "*.blend").Length;

                if (objFiles > 0)
                    return $"找到 {objFiles} 个OBJ文件，但未找到STL文件。请将STL模型放入Resources目录";
                if (blendFiles > 0)
                    return $"找到blender文件，但未找到STL文件。请从Blender导出STL格式";
            }

            return "未找到STL模型，请将 .stl 文件放到 Resources 目录";
        }

        // ==================== 行程控制 ====================
        private Point3D GetPointOnPath(double motorPosition)
        {
            if (_armCenterPath.Count == 0)
            {
                var modelX = MapMotorToModelX(motorPosition);
                return new Point3D(modelX, 0, 0);
            }

            if (_armCenterPath.Count == 1)
            {
                return _armCenterPath[0];
            }

            var t = (motorPosition - MotorMin) / (MotorMax - MotorMin);
            t = Math.Max(0, Math.Min(1, t));

            t = PATH_START_OFFSET + t * (1.0 - PATH_START_OFFSET);

            var targetDistance = t * _pathLength;
            var accumulatedDistance = 0.0;

            for (int i = 1; i < _armCenterPath.Count; i++)
            {
                var segmentLength = (_armCenterPath[i] - _armCenterPath[i - 1]).Length;

                if (accumulatedDistance + segmentLength >= targetDistance)
                {
                    var segmentT = (targetDistance - accumulatedDistance) / segmentLength;
                    return LerpPoint3D(_armCenterPath[i - 1], _armCenterPath[i], segmentT);
                }

                accumulatedDistance += segmentLength;
            }

            return _armCenterPath[_armCenterPath.Count - 1];
        }

        private Point3D LerpPoint3D(Point3D a, Point3D b, double t)
        {
            return new Point3D(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t
            );
        }

        public double MapMotorToModelX(double motorPos)
        {
            var clamped = Math.Max(MotorMin, Math.Min(MotorMax, motorPos));
            var t = (clamped - MotorMin) / (MotorMax - MotorMin);
            return _modelXMin + t * (_modelXMax - _modelXMin);
        }

        public void UpdateIndicatorByRegion(double modelX)
        {
            var range = _modelXMax - _modelXMin;
            var third = range / 3.0;
            string region;
            Color color;

            if (modelX >= _modelXMin + 2 * third)
            {
                region = "手腕";
                color = Colors.DodgerBlue;
            }
            else if (modelX >= _modelXMin + third)
            {
                region = "前臂";
                color = Colors.LimeGreen;
            }
            else
            {
                region = "上臂";
                color = Color.FromRgb(255, 99, 71);
            }

            _regionText.Text = $"区域: {region}";

            var mainBrush = new SolidColorBrush(color);
            var haloBrush = new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B));

            _mainRing.Fill = mainBrush;
            _outerHalo.Fill = haloBrush;
        }
    }
}