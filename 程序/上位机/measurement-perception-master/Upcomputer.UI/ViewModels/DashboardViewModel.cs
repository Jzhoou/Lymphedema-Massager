using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Channels;
using Upcomputer.Common.Models;
using Upcomputer.Common.Enums;
using Upcomputer.Core.Models;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Services;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace Upcomputer.UI.ViewModels
{
    public class DashboardViewModel : ObservableObject, IDisposable
    {
        private readonly ICommunicationService _communicationService;
        private readonly TreatmentEngine _treatmentEngine;
        private readonly ITreatmentRepository _treatmentRepository;
        private readonly IPressureLogRepository _pressureLogRepository;
        private readonly ILogger<DashboardViewModel> _logger;
        private readonly ObservableCollection<PressureData> _pressureDataList = new();
        private readonly ObservableCollection<EdemaData> _edemaDataList = new();
        private readonly ObservableCollection<StepperMotorStatus> _stepperMotors = new();
        private readonly ObservableCollection<ServoStatus> _servos = new();
        private readonly ObservableCollection<string> _chartModes = new() { "水肿阻抗", "压力数据" };
        private readonly ObservableCollection<EdemaDataPoint> _edemaHistory = new();
        private readonly ObservableCollection<PressureChartPoint> _pressureChartHistory = new();
        private static readonly double[] PressureFusionWeights = { 1.0, 1.1, 1.2, 1.2, 1.1, 1.0 };
        private readonly object _syncLock = new();
        private readonly List<PressureChartPoint> _pendingPressureChartPoints = new();
        private readonly List<PressureData> _pendingPressureLogs = new();
        private readonly List<EdemaData> _pendingEdemaLogs = new();
        private readonly double[] _lastPressureLogValues = new double[6];
        private readonly double[] _lastPressureLogMaxValues = new double[6];
        private readonly double[] _lastPressureLogThresholds = new double[6];
        private readonly bool[] _lastPressureLogAlerts = new bool[6];
        private double _lastEdemaLogImpedance;
        private double _lastEdemaLogPercentage;
        private double _lastEdemaLogThreshold;
        private bool _lastEdemaLogAlert;
        private readonly double[] _latestPressureValues = new double[6];
        private readonly double[] _latestPressureThresholds = new double[6];
        private readonly double[] _latestPressureMaxValues = new double[6];
        private readonly bool[] _latestPressureAlerts = new bool[6];
        private double _latestEdemaImpedance;
        private double _latestEdemaPercentage;
        private double _latestEdemaThreshold = 30;
        private bool _latestEdemaAlert;
        private double _currentTcpLatencyMs;
        private double _currentUdpLatencyMs;
        private double _currentSerialLatencyMs;
        private bool _isSerialTransport;
        private readonly System.Threading.Timer _dataCompletionTimer;
        private readonly DispatcherTimer _uiRefreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly Channel<DbWriteRequest> _dbWriteChannel = Channel.CreateUnbounded<DbWriteRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        private readonly Task _dbWriterTask;
        private TreatmentSession _currentSession = new();
        private System.Collections.IEnumerable _selectedChartHistory = Array.Empty<object>();
        private string _selectedChartMode = "水肿阻抗";
        private string _selectedChartYAxisTitle = "阻抗 (Ω)";
        private string _selectedChartSeriesLabel = "水肿阻抗";
        private bool _pausedDueToConnectionLoss;
        private bool _hasPressureData;
        private bool _hasPressureLogAnchor;
        private bool _isDeviceConnected;
        private Guid _lastPressureSessionId = Guid.Empty;
        private int _captureRunning;
        private int _flushRunning;
        private DateTime _lastPressureLogTimestamp;
        private DateTime _lastEdemaLogTimestamp;
        private int _edemaSampleIndex;
        private const int TherapyProgressResyncThresholdSeconds = 5;

        public DashboardViewModel(ICommunicationService communicationService, TreatmentEngine treatmentEngine, ITreatmentRepository treatmentRepository, IPressureLogRepository pressureLogRepository, ILogger<DashboardViewModel> logger)
        {
            _communicationService = communicationService;
            _treatmentEngine = treatmentEngine;
            _treatmentRepository = treatmentRepository;
            _pressureLogRepository = pressureLogRepository;
            _logger = logger;
            _treatmentEngine.SessionUpdated += OnSessionUpdated;
            _communicationService.PressureDataReceived += OnPressureDataReceived;
            _communicationService.EdemaDataReceived += OnEdemaDataReceived;
            _communicationService.MotorStatusReceived += OnMotorStatusReceived;
            _communicationService.TherapyProgressReceived += OnTherapyProgressReceived;
            _communicationService.SystemStatusChanged += OnSystemStatusChanged;
            _dbWriterTask = Task.Run(ProcessDbWriteQueueAsync);

            // 初始化6路压力传感器
            for (int i = 1; i <= 6; i++)
            {
                _pressureDataList.Add(new PressureData
                {
                    SensorId = i,
                    Threshold = 80,
                    MaxValue = 100
                });
            }

            // 初始化1路水肿传感器
            for (int i = 1; i <= 1; i++)
            {
                _edemaDataList.Add(new EdemaData
                {
                    SensorId = i,
                    Threshold = 30
                });
            }

            // 初始化步进电机
            for (int i = 1; i <= 2; i++)
            {
                _stepperMotors.Add(new StepperMotorStatus { MotorId = i });
            }

            // 初始化舵机
            for (int i = 1; i <= 6; i++)
            {
                _servos.Add(new ServoStatus { ServoId = i });
            }

            UpdateSelectedChartSource();

            _dataCompletionTimer = new System.Threading.Timer(CaptureSyntheticData, null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
            _uiRefreshTimer.Tick += FlushPendingChartData;
            _uiRefreshTimer.Start();
        }

        public ObservableCollection<PressureData> PressureDataList => _pressureDataList;
        public ObservableCollection<EdemaData> EdemaDataList => _edemaDataList;
        public ObservableCollection<StepperMotorStatus> StepperMotors => _stepperMotors;
        public ObservableCollection<ServoStatus> Servos => _servos;
        public ObservableCollection<string> ChartModes => _chartModes;
        public ObservableCollection<PressureChartPoint> PressureChartHistory => _pressureChartHistory;
        public ObservableCollection<EdemaDataPoint> EdemaHistory => _edemaHistory;

        public TreatmentSession CurrentSession
        {
            get => _currentSession;
            set => SetField(ref _currentSession, value);
        }

        public string SelectedChartMode
        {
            get => _selectedChartMode;
            set
            {
                if (SetField(ref _selectedChartMode, value))
                {
                    UpdateSelectedChartSource();
                }
            }
        }

        public System.Collections.IEnumerable SelectedChartHistory
        {
            get => _selectedChartHistory;
            private set => SetField(ref _selectedChartHistory, value);
        }

        public string SelectedChartYAxisTitle
        {
            get => _selectedChartYAxisTitle;
            private set => SetField(ref _selectedChartYAxisTitle, value);
        }

        public string SelectedChartSeriesLabel
        {
            get => _selectedChartSeriesLabel;
            private set => SetField(ref _selectedChartSeriesLabel, value);
        }

        private void OnSessionUpdated(object? sender, TreatmentSession session)
        {
            if (_lastPressureSessionId != session.SessionId)
            {
                ResetSyntheticLogAnchors();
                _lastPressureSessionId = session.SessionId;
            }

            if (!session.IsRunning && session.ElapsedTime >= session.TotalDuration)
            {
                _pausedDueToConnectionLoss = false;
            }

            InvokeOnUiThread(() => CurrentSession = session);
        }

        private Task PersistPressureReportAsync(PressureDataReport report)
        {
            try
            {
                var sessionId = _treatmentEngine.CurrentSession.SessionId;
                if (sessionId == Guid.Empty || report.Items.Count == 0)
                {
                    return Task.CompletedTask;
                }

                var snapshot = new PressureReportSnapshot(
                    sessionId,
                    DateTime.Now,
                    report.Items.Select(item => new PressureItemSnapshot(
                        item.SensorId,
                        item.CurrentValue,
                        item.MaxValue,
                        item.Threshold,
                        item.AlertFlag)).ToArray());

                EnqueueDbWrite(new PressureReportWriteRequest(snapshot));
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "保存压力日志失败。");
                return Task.CompletedTask;
            }
        }

        private Task PersistPressureSnapshotAsync()
        {
            try
            {
                var sessionId = _treatmentEngine.CurrentSession.SessionId;
                if (sessionId == Guid.Empty)
                {
                    return Task.CompletedTask;
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "保存压力补全日志失败。");
                return Task.CompletedTask;
            }
        }

        private Task PersistEdemaReportAsync(EdemaDataReport report)
        {
            try
            {
                var sessionId = _treatmentEngine.CurrentSession.SessionId;
                if (sessionId == Guid.Empty)
                {
                    return Task.CompletedTask;
                }

                var snapshot = new EdemaReportSnapshot(
                    sessionId,
                    DateTime.Now,
                    report.SensorId,
                    report.Impedance,
                    report.EdemaPercent,
                    report.AlertFlag,
                    _edemaDataList.ElementAtOrDefault(report.SensorId - 1)?.Threshold ?? 30);

                EnqueueDbWrite(new EdemaReportWriteRequest(snapshot));
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "保存水肿日志失败。");
                return Task.CompletedTask;
            }
        }

        private void EnqueueDbWrite(DbWriteRequest request)
        {
            if (!_dbWriteChannel.Writer.TryWrite(request))
            {
                _logger.LogDebug("数据库写入队列已关闭，丢弃一条待写入数据。");
            }
        }

        private async Task ProcessDbWriteQueueAsync()
        {
            var pressureBatch = new List<PressureData>(256);
            var edemaBatch = new List<EdemaData>(128);

            try
            {
                await foreach (var request in _dbWriteChannel.Reader.ReadAllAsync())
                {
                    ProcessDbWriteRequest(request, pressureBatch, edemaBatch);

                    while (_dbWriteChannel.Reader.TryRead(out var nextRequest))
                    {
                        ProcessDbWriteRequest(nextRequest, pressureBatch, edemaBatch);
                    }

                    await FlushDbBatchesAsync(pressureBatch, edemaBatch).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "数据库后台写入循环异常退出。");
            }
            finally
            {
                await FlushDbBatchesAsync(pressureBatch, edemaBatch).ConfigureAwait(false);
            }
        }

        private void ProcessDbWriteRequest(DbWriteRequest request, List<PressureData> pressureBatch, List<EdemaData> edemaBatch)
        {
            switch (request)
            {
                case PressureReportWriteRequest pressureRequest:
                    BuildPressureLogBatch(pressureRequest.Snapshot, pressureBatch);
                    break;
                case EdemaReportWriteRequest edemaRequest:
                    BuildEdemaLogBatch(edemaRequest.Snapshot, edemaBatch);
                    break;
            }
        }

        private void BuildPressureLogBatch(PressureReportSnapshot snapshot, List<PressureData> pressureBatch)
        {
            lock (_syncLock)
            {
                if (_hasPressureLogAnchor)
                {
                    var nextTimestamp = _lastPressureLogTimestamp.AddMilliseconds(200);
                    while (nextTimestamp < snapshot.Timestamp)
                    {
                        for (var i = 0; i < _lastPressureLogValues.Length; i++)
                        {
                            pressureBatch.Add(new PressureData
                            {
                                SessionId = snapshot.SessionId,
                                SensorId = i + 1,
                                CurrentValue = _lastPressureLogValues[i],
                                MaxValue = _lastPressureLogMaxValues[i],
                                Threshold = _lastPressureLogThresholds[i],
                                IsAlert = _lastPressureLogAlerts[i],
                                LatencyMs = GetTransportLatencyMs(),
                                Timestamp = nextTimestamp
                            });
                        }

                        nextTimestamp = nextTimestamp.AddMilliseconds(200);
                    }
                }

                foreach (var item in snapshot.Items)
                {
                    pressureBatch.Add(new PressureData
                    {
                        SessionId = snapshot.SessionId,
                        SensorId = item.SensorId,
                        CurrentValue = item.CurrentValue,
                        MaxValue = item.MaxValue,
                        Threshold = item.Threshold,
                        IsAlert = item.AlertFlag != 0,
                        LatencyMs = GetTransportLatencyMs(),
                        Timestamp = snapshot.Timestamp
                    });
                }

                for (var i = 0; i < _lastPressureLogValues.Length; i++)
                {
                    var item = snapshot.Items.FirstOrDefault(x => x.SensorId == i + 1);
                    if (item == null)
                    {
                        continue;
                    }

                    _lastPressureLogValues[i] = item.CurrentValue;
                    _lastPressureLogMaxValues[i] = item.MaxValue;
                    _lastPressureLogThresholds[i] = item.Threshold;
                    _lastPressureLogAlerts[i] = item.AlertFlag != 0;
                }

                _lastPressureLogTimestamp = snapshot.Timestamp;
                _hasPressureLogAnchor = true;
            }
        }

        private void BuildEdemaLogBatch(EdemaReportSnapshot snapshot, List<EdemaData> edemaBatch)
        {
            lock (_syncLock)
            {
                edemaBatch.Add(new EdemaData
                {
                    SessionId = snapshot.SessionId,
                    SensorId = snapshot.SensorId,
                    Impedance = snapshot.Impedance,
                    EdemaPercentage = snapshot.EdemaPercentage,
                    Threshold = snapshot.Threshold,
                    IsAlert = snapshot.AlertFlag != 0,
                    LatencyMs = GetTransportLatencyMs(),
                    Timestamp = snapshot.Timestamp
                });

                _lastEdemaLogImpedance = snapshot.Impedance;
                _lastEdemaLogPercentage = snapshot.EdemaPercentage;
                _lastEdemaLogThreshold = snapshot.Threshold;
                _lastEdemaLogAlert = snapshot.AlertFlag != 0;
                _lastEdemaLogTimestamp = snapshot.Timestamp;
            }
        }

        private async Task FlushDbBatchesAsync(List<PressureData> pressureBatch, List<EdemaData> edemaBatch)
        {
            try
            {
                if (pressureBatch.Count > 0)
                {
                    foreach (var batch in pressureBatch.Chunk(100))
                    {
                        await _pressureLogRepository.SavePressureDataBatchAsync(batch.ToList()).ConfigureAwait(false);
                    }

                    pressureBatch.Clear();
                }

                if (edemaBatch.Count > 0)
                {
                    var sessionId = edemaBatch[0].SessionId;
                    foreach (var batch in edemaBatch.Chunk(100))
                    {
                        await _treatmentRepository.AddEdemaLogsAsync(sessionId, batch.ToList()).ConfigureAwait(false);
                    }

                    edemaBatch.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "后台批量写入数据库失败。");
            }
        }

        private Task PersistEdemaSnapshotAsync()
        {
            // 水肿数据仅按真实采样写入，不做自动补全。
            return Task.CompletedTask;
        }

        private void OnSystemStatusChanged(object? sender, SystemStatus status)
        {
            InvokeOnUiThread(() =>
            {
                _currentTcpLatencyMs = status.TcpLatency;
                _currentUdpLatencyMs = status.UdpLatency;
                _currentSerialLatencyMs = status.SerialLatency;
                _isSerialTransport = status.IsSerialTransport;

                var hasChannelConnected = status.WifiState == ConnectionState.Connected || status.SerialState == ConnectionState.Connected;
                var isConnected = hasChannelConnected && status.IsDeviceConnected;
                UpdateDeviceConnectionState(isConnected);

                if (!isConnected)
                {
                    if (CurrentSession.IsRunning && CurrentSession.TotalDuration > 0 && CurrentSession.ElapsedTime < CurrentSession.TotalDuration)
                    {
                        _pausedDueToConnectionLoss = true;
                        _treatmentEngine.PauseTreatment();
                    }

                    return;
                }

                if (_pausedDueToConnectionLoss && !CurrentSession.IsRunning && CurrentSession.ElapsedTime < CurrentSession.TotalDuration)
                {
                    _pausedDueToConnectionLoss = false;
                    _treatmentEngine.ResumeTreatment();
                }
            });
        }

        private void OnPressureDataReceived(object? sender, PressureDataReport report)
        {
            InvokeOnUiThread(() =>
            {
                if (report.Items.Count == 0)
                {
                    return;
                }

                foreach (var item in report.Items)
                {
                    var index = item.SensorId - 1;
                    if (index < 0 || index >= _pressureDataList.Count)
                    {
                        continue;
                    }

                    var sensor = _pressureDataList[index];
                    sensor.Threshold = item.Threshold;
                    sensor.MaxValue = item.MaxValue;
                    sensor.CurrentValue = item.CurrentValue;
                    sensor.IsAlert = item.AlertFlag != 0;
                }

                RefreshLatestPressureCache();
                _hasPressureData = true;
                UpdateDeviceConnectionState(true);
                AppendPressureSnapshot();
            });

            _ = PersistPressureReportAsync(report);
        }

        private void OnEdemaDataReceived(object? sender, EdemaDataReport report)
        {
            InvokeOnUiThread(() =>
            {
                var index = report.SensorId - 1;
                if (index < 0 || index >= _edemaDataList.Count)
                {
                    return;
                }

                var sensor = _edemaDataList[index];
                sensor.Impedance = report.Impedance;
                sensor.EdemaPercentage = report.EdemaPercent;

                RefreshLatestEdemaCache();
                UpdateDeviceConnectionState(true);
                AppendEdemaHistoryPoint(Interlocked.Increment(ref _edemaSampleIndex), _edemaDataList.Average(x => x.Impedance));
            });

            _ = PersistEdemaReportAsync(report);
        }

        private void OnTherapyProgressReceived(object? sender, TherapyProgressReport report)
        {
            InvokeOnUiThread(() =>
            {
                var deviceTotalDuration = report.ElapsedTime + report.RemainingTime;
                if (deviceTotalDuration > 0 && CurrentSession.TotalDuration <= 0)
                {
                    CurrentSession.TotalDuration = deviceTotalDuration;
                }

                var localElapsed = CurrentSession.ElapsedTime;
                var deviceElapsed = (int)report.ElapsedTime;
                var drift = Math.Abs(deviceElapsed - localElapsed);
                var shouldResyncToDevice = drift > TherapyProgressResyncThresholdSeconds;

                if (shouldResyncToDevice)
                {
                    if (deviceTotalDuration > 0)
                    {
                        CurrentSession.TotalDuration = deviceTotalDuration;
                    }

                    var maxElapsed = Math.Max(0, CurrentSession.TotalDuration);
                    CurrentSession.ElapsedTime = Math.Min(maxElapsed, Math.Max(0, deviceElapsed));
                }

                CurrentSession.IsRunning = report.RemainingTime > 0;
            });
        }

        private void OnMotorStatusReceived(object? sender, MotorStatusReport report)
        {
            InvokeOnUiThread(() =>
            {
                if (_stepperMotors.Count >= 1)
                {
                    var motor1 = _stepperMotors[0];
                    motor1.IsConnected = true;
                    motor1.State = MapMotorState(report.Stepper1State);
                    motor1.Speed = report.Stepper1Speed;
                    motor1.Direction = report.Stepper1Speed > 0;
                }

                if (_stepperMotors.Count >= 2)
                {
                    var motor2 = _stepperMotors[1];
                    motor2.IsConnected = true;
                    motor2.State = MapMotorState(report.Stepper2State);
                    motor2.Speed = report.Stepper2Speed;
                    motor2.Direction = report.Stepper2Speed > 0;
                }

                for (var i = 0; i < _servos.Count; i++)
                {
                    if (i < report.ServoAngles.Length)
                    {
                        _servos[i].Angle = report.ServoAngles[i];
                    }

                    _servos[i].IsConnected = true;
                    _servos[i].IsAtLimit = false;
                    _servos[i].IsHomed = true;
                }
            });
        }

        private void UpdateDeviceConnectionState(bool isConnected)
        {
            _isDeviceConnected = isConnected;

            foreach (var motor in _stepperMotors)
            {
                motor.IsConnected = isConnected;
            }

            foreach (var servo in _servos)
            {
                servo.IsConnected = isConnected;
            }
        }

        private void RefreshLatestPressureCache()
        {
            lock (_syncLock)
            {
                for (var i = 0; i < _pressureDataList.Count; i++)
                {
                    var sensor = _pressureDataList[i];
                    _latestPressureValues[i] = sensor.CurrentValue;
                    _latestPressureThresholds[i] = sensor.Threshold;
                    _latestPressureMaxValues[i] = sensor.MaxValue;
                    _latestPressureAlerts[i] = sensor.IsAlert;
                }
            }
        }

        private void RefreshLatestEdemaCache()
        {
            lock (_syncLock)
            {
                var sensor = _edemaDataList.FirstOrDefault();
                if (sensor == null)
                {
                    return;
                }

                _latestEdemaImpedance = sensor.Impedance;
                _latestEdemaPercentage = sensor.EdemaPercentage;
                _latestEdemaThreshold = sensor.Threshold;
                _latestEdemaAlert = sensor.IsAlert;
            }
        }

        private void AppendEdemaHistoryPoint(int sampleIndex, double value)
        {
            _edemaHistory.Add(new EdemaDataPoint(sampleIndex, value));

            while (_edemaHistory.Count > 100)
            {
                _edemaHistory.RemoveAt(0);
            }
        }

        private void AppendPressureSnapshot()
        {
            AppendPressureSnapshot(DateTime.Now);
        }

        private void AppendPressureSnapshot(DateTime timestamp)
        {
            var values = _pressureDataList.Select(x => x.CurrentValue).ToArray();
            var weightedAverage = CalculateWeightedAverage(values);

            _pressureChartHistory.Add(new PressureChartPoint
            {
                Time = timestamp,
                Sensor1 = values.ElementAtOrDefault(0),
                Sensor2 = values.ElementAtOrDefault(1),
                Sensor3 = values.ElementAtOrDefault(2),
                Sensor4 = values.ElementAtOrDefault(3),
                Sensor5 = values.ElementAtOrDefault(4),
                Sensor6 = values.ElementAtOrDefault(5),
                FusedPressure = weightedAverage
            });

            while (_pressureChartHistory.Count > 100)
            {
                _pressureChartHistory.RemoveAt(0);
            }
        }

        private void FlushPendingChartData(object? sender, EventArgs e)
        {
            List<PressureChartPoint> pressurePoints;

            lock (_syncLock)
            {
                if (_pendingPressureChartPoints.Count == 0)
                {
                    return;
                }

                pressurePoints = new List<PressureChartPoint>(_pendingPressureChartPoints);
                _pendingPressureChartPoints.Clear();
            }

            foreach (var point in pressurePoints)
            {
                _pressureChartHistory.Add(point);

                while (_pressureChartHistory.Count > 100)
                {
                    _pressureChartHistory.RemoveAt(0);
                }
            }

        }

        private void FlushPendingLogs(object? state)
        {
            _ = FlushPendingLogsAsync();
        }

        private async Task FlushPendingLogsAsync()
        {
            if (Interlocked.Exchange(ref _flushRunning, 1) == 1)
            {
                return;
            }

            try
            {
                List<PressureData> pressureLogs;
                List<EdemaData> edemaLogs;

                lock (_syncLock)
                {
                    if (_pendingPressureLogs.Count == 0 && _pendingEdemaLogs.Count == 0)
                    {
                        return;
                    }

                    pressureLogs = new List<PressureData>(_pendingPressureLogs);
                    edemaLogs = new List<EdemaData>(_pendingEdemaLogs);
                    _pendingPressureLogs.Clear();
                    _pendingEdemaLogs.Clear();
                }

                if (pressureLogs.Count > 0)
                {
                    foreach (var batch in pressureLogs.Chunk(100))
                    {
                        await _pressureLogRepository.SavePressureDataBatchAsync(batch.ToList()).ConfigureAwait(false);
                        await Task.Yield();
                    }
                }

                if (edemaLogs.Count > 0)
                {
                    var sessionId = pressureLogs.FirstOrDefault()?.SessionId ?? edemaLogs[0].SessionId;
                    foreach (var batch in edemaLogs.Chunk(100))
                    {
                        await _treatmentRepository.AddEdemaLogsAsync(sessionId, batch.ToList()).ConfigureAwait(false);
                        await Task.Yield();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量保存补全日志失败。");
            }
            finally
            {
                Interlocked.Exchange(ref _flushRunning, 0);
            }
        }

        private void CaptureSyntheticData(object? state)
        {
            if (Interlocked.Exchange(ref _captureRunning, 1) == 1)
            {
                return;
            }

            try
            {
                if (!_isDeviceConnected || !CurrentSession.IsRunning)
                {
                    return;
                }

                var sessionId = _treatmentEngine.CurrentSession.SessionId;
                if (sessionId == Guid.Empty)
                {
                    return;
                }

                var timestamp = DateTime.Now;

                lock (_syncLock)
                {
                    if (_hasPressureData)
                    {
                        var values = _latestPressureValues.ToArray();
                        _pendingPressureChartPoints.Add(new PressureChartPoint
                        {
                            Time = timestamp,
                            Sensor1 = values.ElementAtOrDefault(0),
                            Sensor2 = values.ElementAtOrDefault(1),
                            Sensor3 = values.ElementAtOrDefault(2),
                            Sensor4 = values.ElementAtOrDefault(3),
                            Sensor5 = values.ElementAtOrDefault(4),
                            Sensor6 = values.ElementAtOrDefault(5),
                            FusedPressure = CalculateWeightedAverage(values)
                        });
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成补全数据失败。");
            }
            finally
            {
                Interlocked.Exchange(ref _captureRunning, 0);
            }
        }

        private static double CalculateWeightedAverage(double[] values)
        {
            if (values.Length == 0) return 0;

            var count = Math.Min(values.Length, PressureFusionWeights.Length);
            var weightedSum = 0d;
            var totalWeight = 0d;

            for (var i = 0; i < count; i++)
            {
                weightedSum += values[i] * PressureFusionWeights[i];
                totalWeight += PressureFusionWeights[i];
            }

            return totalWeight > 0 ? weightedSum / totalWeight : 0;
        }

        private void UpdateSelectedChartSource()
        {
            if (SelectedChartMode == "压力数据")
            {
                SelectedChartHistory = _pressureChartHistory;
                SelectedChartYAxisTitle = "压力值 (g)";
                SelectedChartSeriesLabel = "压力值";
            }
            else
            {
                SelectedChartHistory = _edemaHistory;
                SelectedChartYAxisTitle = "阻抗 (Ω)";
                SelectedChartSeriesLabel = "水肿阻抗";
            }
        }

        private static void InvokeOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Invoke(action);
        }

        private double GetTransportLatencyMs()
        {
            if (_isSerialTransport)
            {
                return _currentSerialLatencyMs;
            }

            return _currentUdpLatencyMs > 0 ? _currentUdpLatencyMs : _currentTcpLatencyMs;
        }

        private void ResetSyntheticLogAnchors()
        {
            lock (_syncLock)
            {
                _hasPressureLogAnchor = false;
                _lastPressureLogTimestamp = default;
                _lastEdemaLogTimestamp = default;
                Array.Clear(_lastPressureLogValues, 0, _lastPressureLogValues.Length);
                Array.Clear(_lastPressureLogMaxValues, 0, _lastPressureLogMaxValues.Length);
                Array.Clear(_lastPressureLogThresholds, 0, _lastPressureLogThresholds.Length);
                Array.Clear(_lastPressureLogAlerts, 0, _lastPressureLogAlerts.Length);
                _lastEdemaLogImpedance = 0;
                _lastEdemaLogPercentage = 0;
                _lastEdemaLogThreshold = 0;
                _lastEdemaLogAlert = false;
                _edemaSampleIndex = 0;
            }
        }

        private static MotorState MapMotorState(byte state) => state switch
        {
            1 => MotorState.Running,
            2 => MotorState.Error,
            _ => MotorState.Stopped
        };

        public void Dispose()
        {
            _dataCompletionTimer.Dispose();
            ResetSyntheticLogAnchors();
            _dbWriteChannel.Writer.TryComplete();
            try
            {
                _dbWriterTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }
            _uiRefreshTimer.Stop();
            _uiRefreshTimer.Tick -= FlushPendingChartData;

            FlushPendingChartData(null, EventArgs.Empty);
            _treatmentEngine.SessionUpdated -= OnSessionUpdated;
            _communicationService.PressureDataReceived -= OnPressureDataReceived;
            _communicationService.EdemaDataReceived -= OnEdemaDataReceived;
            _communicationService.MotorStatusReceived -= OnMotorStatusReceived;
            _communicationService.TherapyProgressReceived -= OnTherapyProgressReceived;
            _communicationService.SystemStatusChanged -= OnSystemStatusChanged;
        }

        private abstract record DbWriteRequest;

        private sealed record PressureReportWriteRequest(PressureReportSnapshot Snapshot) : DbWriteRequest;

        private sealed record EdemaReportWriteRequest(EdemaReportSnapshot Snapshot) : DbWriteRequest;

        private sealed record PressureReportSnapshot(Guid SessionId, DateTime Timestamp, PressureItemSnapshot[] Items);

        private sealed record PressureItemSnapshot(byte SensorId, double CurrentValue, double MaxValue, double Threshold, byte AlertFlag);

        private sealed record EdemaReportSnapshot(Guid SessionId, DateTime Timestamp, byte SensorId, double Impedance, double EdemaPercentage, byte AlertFlag, double Threshold);
    }

    public class EdemaDataPoint
    {
        public DateTime Time { get; set; }
        public double Value { get; set; }
        public int SampleIndex { get; set; }

        public string SampleDisplay => SampleIndex.ToString();

        public EdemaDataPoint(int sampleIndex, double value)
        {
            SampleIndex = sampleIndex;
            Value = value;
        }
    }

    public class PressureChartPoint
    {
        public DateTime Time { get; set; }
        public double Sensor1 { get; set; }
        public double Sensor2 { get; set; }
        public double Sensor3 { get; set; }
        public double Sensor4 { get; set; }
        public double Sensor5 { get; set; }
        public double Sensor6 { get; set; }
        public double FusedPressure { get; set; }

        public string TimeDisplay => Time.ToString("HH:mm:ss");
    }
}
