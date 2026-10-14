using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Upcomputer.Common.Enums;
using Upcomputer.Common.Models;
using Upcomputer.Communication.Protocol;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;
using Upcomputer.Core.Services;
using Microsoft.Extensions.Logging;



namespace Upcomputer.UI.ViewModels
{
    public class ControlPanelViewModel : ObservableObject, IDisposable
    {
        private readonly ICommunicationService _communicationService;
        private readonly TreatmentEngine _treatmentEngine;
        private readonly ITreatmentRepository _treatmentRepository;
        private readonly ILogger<ControlPanelViewModel> _logger;

        private TherapyMode _selectedMode = TherapyMode.Rehabilitation;
        private double _intensity = 50;
        private int _duration = 20;
        private double _stepperSpeed = 100;
        private ObservableCollection<ServoAngleItem> _servoAngles = new();
        private bool _isTreatmentRunning;
        private bool _isTreatmentPaused;
        private bool _isEmergencyStopped;
        private bool _deviceTherapyActive;
        private string _commandResult = string.Empty;
        private bool _isConnected;  // 添加缺失的字段
        private bool _isSerialConnected;
        private string _selectedMotorDirection = "正转";
        private string _motorStepsInput = "100";
        private string _motorSpeedInput = "100";
        private readonly List<CommandLog> _pendingCommandLogs = new();
        private bool _sessionPersisted;
        private bool _connectionLostShown;

        // 命令引用
        // 使用 AsyncRelayCommand
        private AsyncRelayCommand _startCommand;
        private AsyncRelayCommand _stopCommand;
        private AsyncRelayCommand _pauseResumeCommand;
        private AsyncRelayCommand _emergencyStopCommand;
        private AsyncRelayCommand _saveParametersCommand;
        private AsyncRelayCommand _homeAllServosCommand;
        private AsyncRelayCommand _motorHomeCommand;
        private AsyncRelayCommand _motorStepCommand;
        private AsyncRelayCommand _ntpSyncCommand;

        public ControlPanelViewModel(ICommunicationService communicationService, TreatmentEngine treatmentEngine, ITreatmentRepository treatmentRepository, ILogger<ControlPanelViewModel> logger)
        {
            _communicationService = communicationService;
            _treatmentEngine = treatmentEngine;
            _treatmentRepository = treatmentRepository;
            _logger = logger;
            _treatmentEngine.SessionUpdated += OnSessionUpdated;
            _communicationService.SystemStatusChanged += OnSystemStatusChanged;
            _communicationService.PressureDataReceived += OnPressureDataReceived;
            _communicationService.EdemaDataReceived += OnEdemaDataReceived;
            _communicationService.MotorStatusReceived += OnMotorStatusReceived;
            _communicationService.TherapyProgressReceived += OnTherapyProgressReceived;

            // 创建命令
            _startCommand = new AsyncRelayCommand(StartTreatmentAsync, () => CanStartTreatment);
            _stopCommand = new AsyncRelayCommand(StopTreatmentAsync, () => CanStopTreatment);
            _pauseResumeCommand = new AsyncRelayCommand(PauseResumeTreatmentAsync, () => CanPauseOrResumeTreatment);
            _emergencyStopCommand = new AsyncRelayCommand(EmergencyToggleAsync, () => IsConnected || IsEmergencyStopped);
            _saveParametersCommand = new AsyncRelayCommand(SaveParametersAsync, () => IsConnected && !IsEmergencyStopped);
            _homeAllServosCommand = new AsyncRelayCommand(HomeAllServosAsync, () => IsConnected && !IsEmergencyStopped);
            _motorHomeCommand = new AsyncRelayCommand(MotorHomeAsync, () => IsConnected && !IsEmergencyStopped);
            _motorStepCommand = new AsyncRelayCommand(MotorStepAsync, () => IsConnected && !IsEmergencyStopped);
            _ntpSyncCommand = new AsyncRelayCommand(NtpSyncAsync, () => IsConnected && !IsEmergencyStopped);

            // 将属性绑定到命令字段
            StartCommand = _startCommand;
            StopCommand = _stopCommand;
            PauseResumeCommand = _pauseResumeCommand;
            EmergencyStopCommand = _emergencyStopCommand;
            SaveParametersCommand = _saveParametersCommand;
            HomeAllServosCommand = _homeAllServosCommand;
            MotorHomeCommand = _motorHomeCommand;
            MotorStepCommand = _motorStepCommand;
            NtpSyncCommand = _ntpSyncCommand;

            // 初始化舵机角度
            for (int i = 1; i <= 6; i++)
            {
                _servoAngles.Add(new ServoAngleItem { ServoId = i, Angle = 90 });
            }


            // 调试：检查初始状态
            System.Diagnostics.Debug.WriteLine($"[ControlPanelViewModel] 构造函数 - IsConnected={_communicationService.IsConnected}");

            // 手动设置初始状态
            IsConnected = _communicationService.IsConnected;
            IsTreatmentRunning = _deviceTherapyActive || (_treatmentEngine.CurrentSession?.IsRunning ?? false);

            // 主动同步当前状态
            SyncConnectionState();

        }

        /// <summary>
        /// 主动同步连接状态（用于 ViewModel 创建时）
        /// </summary>
        public void SyncConnectionState()
        {
            IsConnected = _communicationService.IsConnected || _isSerialConnected;
            IsTreatmentRunning = _deviceTherapyActive || (_treatmentEngine.CurrentSession?.IsRunning ?? false);

            // 强制通知属性变更
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsTreatmentRunning));

            System.Diagnostics.Debug.WriteLine($"[ControlPanelViewModel] SyncConnectionState - IsConnected={IsConnected}, IsTreatmentRunning={IsTreatmentRunning}");

            RefreshCommandStates();
        }

        public bool CanStartTreatment => IsConnected && !IsTreatmentRunning && !IsEmergencyStopped;

        public bool CanStopTreatment => IsConnected && IsTreatmentRunning && !IsEmergencyStopped;

        public bool CanPauseOrResumeTreatment => IsConnected && IsTreatmentRunning && !IsEmergencyStopped;

        public bool CanToggleEmergency => IsConnected || IsEmergencyStopped;

        /// <summary>
        /// 供 View 调用的刷新方法
        /// </summary>
        public void OnViewActivated()
        {
            System.Diagnostics.Debug.WriteLine("[ControlPanelViewModel] OnViewActivated");
            SyncConnectionState();
        }

        public TherapyMode SelectedMode
        {
            get => _selectedMode;
            set => SetField(ref _selectedMode, value);
        }

        public double Intensity
        {
            get => _intensity;
            set => SetField(ref _intensity, value);
        }

        public int Duration
        {
            get => _duration;
            set => SetField(ref _duration, value);
        }

        public double StepperSpeed
        {
            get => _stepperSpeed;
            set => SetField(ref _stepperSpeed, value);
        }

        public ObservableCollection<ServoAngleItem> ServoAngles
        {
            get => _servoAngles;
            set => SetField(ref _servoAngles, value);
        }

        public bool IsTreatmentRunning
        {
            get => _isTreatmentRunning;
            set
            {
                if (SetField(ref _isTreatmentRunning, value))
                {
                    if (!value)
                    {
                        _isTreatmentPaused = false;
                        OnPropertyChanged(nameof(IsTreatmentPaused));
                        OnPropertyChanged(nameof(PauseResumeButtonText));
                    }

                    OnPropertyChanged(nameof(CanStartTreatment));
                    OnPropertyChanged(nameof(CanStopTreatment));
                    OnPropertyChanged(nameof(CanPauseOrResumeTreatment));
                    // 在 UI 线程刷新命令状态
                    // 确保在 UI 线程上执行
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        RefreshCommandStates();
                        // 强制通知属性变更
                        OnPropertyChanged(nameof(IsTreatmentRunning));
                    }, System.Windows.Threading.DispatcherPriority.Normal);
                }
            }
        }

        /// <summary>
        /// 刷新所有命令的 CanExecute 状态
        /// </summary>



        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetField(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(CanStartTreatment));
                    OnPropertyChanged(nameof(CanStopTreatment));
                    OnPropertyChanged(nameof(CanPauseOrResumeTreatment));
                    OnPropertyChanged(nameof(CanToggleEmergency));
                    System.Diagnostics.Debug.WriteLine($"[ControlPanelViewModel] IsConnected 值已变更: {value}");
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        RefreshCommandStates();
                        OnPropertyChanged(nameof(IsConnected));
                    }, System.Windows.Threading.DispatcherPriority.Normal);
                }
            }
        }

        /// <summary>
        /// 强制刷新所有命令（供 View 调用）
        /// </summary>
        public void RefreshAllCommands()
        {
            System.Diagnostics.Debug.WriteLine($"[ControlPanelViewModel] RefreshAllCommands - IsConnected={IsConnected}");

            // 先同步状态
            IsConnected = _communicationService.IsConnected || _isSerialConnected;
            IsTreatmentRunning = _deviceTherapyActive || (_treatmentEngine.CurrentSession?.IsRunning ?? false);

            // 刷新命令
            RefreshCommandStates();

            // 强制 WPF 重新评估所有命令
            CommandManager.InvalidateRequerySuggested();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 强制通知所有属性变更
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(IsTreatmentRunning));
                OnPropertyChanged(nameof(SelectedMode));
                OnPropertyChanged(nameof(Intensity));
                OnPropertyChanged(nameof(Duration));

                // 刷新命令
                RefreshCommandStates();

                // 强制 WPF 命令系统刷新
                CommandManager.InvalidateRequerySuggested();
            });
        }

        /// <summary>
        /// 刷新所有命令的 CanExecute 状态
        /// </summary>
        private void RefreshCommandStates()
        {
            // 调试输出当前状态
            /*
            System.Diagnostics.Debug.WriteLine($"=== 命令状态刷新 ===");
            System.Diagnostics.Debug.WriteLine($"IsConnected: {IsConnected}");
            System.Diagnostics.Debug.WriteLine($"IsTreatmentRunning: {IsTreatmentRunning}");
            System.Diagnostics.Debug.WriteLine($"StartCommand.CanExecute: {_startCommand?.CanExecute(null)}");
            System.Diagnostics.Debug.WriteLine($"StopCommand.CanExecute: {_stopCommand?.CanExecute(null)}");
            */

            // 在 UI 线程上执行
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => RefreshCommandStates());
                return;
            }

            _startCommand?.RaiseCanExecuteChanged();
            _stopCommand?.RaiseCanExecuteChanged();
            _pauseResumeCommand?.RaiseCanExecuteChanged();
            _emergencyStopCommand?.RaiseCanExecuteChanged();
            _saveParametersCommand?.RaiseCanExecuteChanged();
            _homeAllServosCommand?.RaiseCanExecuteChanged();
            _motorHomeCommand?.RaiseCanExecuteChanged();
            _motorStepCommand?.RaiseCanExecuteChanged();
            _ntpSyncCommand?.RaiseCanExecuteChanged();

            // 强制 WPF 命令系统全局刷新
            CommandManager.InvalidateRequerySuggested();
        }

        public string CommandResult
        {
            get => _commandResult;
            set => SetField(ref _commandResult, value);
        }

        public bool IsTreatmentPaused
        {
            get => _isTreatmentPaused;
            private set
            {
                if (SetField(ref _isTreatmentPaused, value))
                {
                    OnPropertyChanged(nameof(PauseResumeButtonText));
                }
            }
        }

        public bool IsEmergencyStopped
        {
            get => _isEmergencyStopped;
            private set
            {
                if (SetField(ref _isEmergencyStopped, value))
                {
                    OnPropertyChanged(nameof(EmergencyButtonText));
                    OnPropertyChanged(nameof(CanToggleEmergency));
                    OnPropertyChanged(nameof(CanStartTreatment));
                    OnPropertyChanged(nameof(CanStopTreatment));
                    OnPropertyChanged(nameof(CanPauseOrResumeTreatment));
                }
            }
        }

        public string PauseResumeButtonText => IsTreatmentPaused ? "▶ 继续治疗" : "⏸ 暂停治疗";

        public string EmergencyButtonText => IsEmergencyStopped ? "🚪 退出急停" : "🆘 紧急停机";

        public string[] MotorDirectionOptions { get; } = new[] { "正转", "反转" };

        public string SelectedMotorDirection
        {
            get => _selectedMotorDirection;
            set => SetField(ref _selectedMotorDirection, value);
        }

        public string MotorStepsInput
        {
            get => _motorStepsInput;
            set => SetField(ref _motorStepsInput, value);
        }

        public string MotorSpeedInput
        {
            get => _motorSpeedInput;
            set => SetField(ref _motorSpeedInput, value);
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand PauseResumeCommand { get; }
        public ICommand EmergencyStopCommand { get; }
        public ICommand SaveParametersCommand { get; }
        public ICommand HomeAllServosCommand { get; }
        public ICommand MotorHomeCommand { get; }
        public ICommand MotorStepCommand { get; }
        public ICommand NtpSyncCommand { get; }

        public IEnumerable<TherapyMode> TherapyModes => Enum.GetValues<TherapyMode>();

        private void OnSystemStatusChanged(object? sender, SystemStatus status)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _isSerialConnected = status.SerialState == ConnectionState.Connected;
                IsConnected = status.WifiState == ConnectionState.Connected || _isSerialConnected;
            });

            // 设备心跳超时离线时弹框（WiFi 和串口均适用）
            if (status.IsDeviceConnected == false
                && (_connectionLostShown == false))
            {
                if (!string.IsNullOrEmpty(status.LastLog)
                    && (status.LastLog.Contains("心跳") || status.LastLog.Contains("设备离线")))
                {
                    _connectionLostShown = true;
                    MessageBox.Show("设备多次未响应心跳，通信已断开，请检查设备连接。", "设备离线", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // 设备恢复在线时重置标记
            if (status.IsDeviceConnected == true)
            {
                _connectionLostShown = false;
            }
        }

        private void OnPressureDataReceived(object? sender, PressureDataReport report)
        {
            MarkDeviceTherapyActive();
        }

        private void OnEdemaDataReceived(object? sender, EdemaDataReport report)
        {
            MarkDeviceTherapyActive();
        }

        private void OnMotorStatusReceived(object? sender, MotorStatusReport report)
        {
            MarkDeviceTherapyActive();
        }

        private void OnTherapyProgressReceived(object? sender, TherapyProgressReport report)
        {
            MarkDeviceTherapyActive();
        }

        private void MarkDeviceTherapyActive()
        {
            _deviceTherapyActive = true;
            IsTreatmentRunning = true;
            RefreshCommandStates();
        }

        public void SetSerialConnectionState(bool isConnected)
        {
            _isSerialConnected = isConnected;
            IsConnected = _communicationService.IsConnected || _isSerialConnected;
            RefreshCommandStates();
        }

        private async Task StartTreatmentAsync()
        {
            // 开始治疗前先强制触发一次NTP时间同步
            try
            {
                var timeData = CommandBuilder.BuildSetTime();
                await _communicationService.SendCommandAsync(ProtocolCommandCode.SetTime, timeData);
                await Task.Delay(50); // 等待设备处理时间同步
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "治疗前NTP时间同步失败，继续启动治疗");
            }

            _deviceTherapyActive = true;
            IsTreatmentRunning = true;
            IsTreatmentPaused = false;
            _treatmentEngine.StartTreatment(SelectedMode, Duration, Intensity);

            try
            {
                await _treatmentRepository.StartTreatmentSessionAsync(_treatmentEngine.CurrentSession);
                _sessionPersisted = true;
                await FlushPendingCommandLogsAsync(_treatmentEngine.CurrentSession.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存治疗会话开始记录失败。");
            }

            // 发送启动命令到下位机
            // 使用 CommandBuilder 构建数据
            var request = new StartTherapyRequest
            {
                Mode = (TherapyMode)(byte)SelectedMode,
                Intensity = (byte)Intensity,
                Duration = (byte)Duration
            };
            var data = CommandBuilder.BuildStartTherapy(request);

            await RecordCommandAsync("启动治疗", $"模式:{SelectedMode}, 强度:{Intensity:F0}%, 时长:{Duration}分钟");
            await _communicationService.SendCommandAsync(ProtocolCommandCode.StartTherapy, data);

            CommandResult = $"治疗已启动 - 模式:{SelectedMode}, 强度:{Intensity}%, 时长:{Duration}分钟";

            // 执行完成后强制刷新
            // 确保 UI 线程刷新
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 强制更新
                OnPropertyChanged(nameof(IsTreatmentRunning));
                OnPropertyChanged(nameof(IsConnected));
                RefreshCommandStates();
                CommandManager.InvalidateRequerySuggested();
            });
        }

        private async Task StopTreatmentAsync()
        {
            await RecordCommandAsync("停止治疗", "停止当前治疗会话");
            _deviceTherapyActive = false;
            _treatmentEngine.StopTreatment();
            IsTreatmentRunning = false;
            IsTreatmentPaused = false;

            try
            {
                await _treatmentRepository.EndTreatmentSessionAsync(_treatmentEngine.CurrentSession.SessionId, DateTime.Now);
                _sessionPersisted = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存治疗会话结束记录失败。");
            }

            // 发送停止命令
            await _communicationService.SendCommandAsync(ProtocolCommandCode.StopTherapy, null);

            CommandResult = "治疗已停止";

            // 执行完成后强制刷新
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(IsTreatmentRunning));
                OnPropertyChanged(nameof(IsConnected));
                RefreshCommandStates();
                CommandManager.InvalidateRequerySuggested();
            });
        }

        private async Task PauseResumeTreatmentAsync()
        {
            if (IsTreatmentPaused)
            {
                await RecordCommandAsync("继续治疗", "恢复当前治疗会话");
                await _communicationService.SendCommandAsync(ProtocolCommandCode.ResumeTherapy, null);
                IsTreatmentPaused = false;
                CommandResult = "治疗已继续";
            }
            else
            {
                await RecordCommandAsync("暂停治疗", "暂停当前治疗会话");
                await _communicationService.SendCommandAsync(ProtocolCommandCode.PauseTherapy, null);
                IsTreatmentPaused = true;
                CommandResult = "治疗已暂停";
            }

            RefreshCommandStates();
        }

        private async Task EmergencyToggleAsync()
        {
            if (IsEmergencyStopped)
            {
                await RecordCommandAsync("退出急停", "退出急停状态");
                await _communicationService.SendCommandAsync(ProtocolCommandCode.ExitEmergencyStop, null);
                IsEmergencyStopped = false;
                CommandResult = "已退出急停状态";
            }
            else
            {
                await RecordCommandAsync("紧急停机", "紧急停止治疗");
                _deviceTherapyActive = false;
                _treatmentEngine.StopTreatment();
                IsTreatmentRunning = false;
                IsTreatmentPaused = false;

                try
                {
                    await _treatmentRepository.EndTreatmentSessionAsync(_treatmentEngine.CurrentSession.SessionId, DateTime.Now);
                    _sessionPersisted = false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "保存紧急停机会话记录失败。");
                }

                await _communicationService.SendCommandAsync(ProtocolCommandCode.EmergencyStop, null);
                IsEmergencyStopped = true;
                CommandResult = "紧急停机已执行";
            }

            RefreshCommandStates();
        }

        private async Task MotorHomeAsync()
        {
            await RecordCommandAsync("电机回零", "执行电机回零");
            await _communicationService.SendCommandAsync(ProtocolCommandCode.MotorHome, null);
            CommandResult = "电机回零命令已发送";
        }

        private async Task NtpSyncAsync()
        {
            try
            {
                var timeData = CommandBuilder.BuildSetTime();
                await _communicationService.SendCommandAsync(ProtocolCommandCode.SetTime, timeData);
                CommandResult = "NTP时间同步命令已发送";
            }
            catch (Exception ex)
            {
                CommandResult = $"NTP时间同步失败: {ex.Message}";
            }
        }

        private async Task MotorStepAsync()
        {
            if (!uint.TryParse(MotorStepsInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steps) || steps == 0)
            {
                CommandResult = "步数输入无效，请输入大于0的整数";
                return;
            }

            if (!double.TryParse(MotorSpeedInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var speedRpm))
            {
                CommandResult = "速度输入无效，请输入数字";
                return;
            }

            if (speedRpm < 0.1 || speedRpm > 3000)
            {
                CommandResult = "速度范围必须在 0.1 - 3000 RPM";
                return;
            }

            byte direction = SelectedMotorDirection == "反转" ? (byte)0x01 : (byte)0x00;
            ushort speed = (ushort)Math.Round(speedRpm, MidpointRounding.AwayFromZero);

            var data = CommandBuilder.BuildMotorStep(new MotorStepRequest
            {
                Direction = direction,
                Steps = steps,
                SpeedRpm = speed
            });

            await RecordCommandAsync("电机步进运动", $"方向:{SelectedMotorDirection}, 步数:{steps}, 速度:{speedRpm:F1}RPM");
            await _communicationService.SendCommandAsync(ProtocolCommandCode.MotorStep, data);
            CommandResult = $"电机步进命令已发送 - 方向:{SelectedMotorDirection}, 步数:{steps}, 速度:{speedRpm:F1}RPM";
        }

        private async Task SaveParametersAsync()
        {
            try
            {
                // 发送模式设置命令
                var modeData = CommandBuilder.BuildSetMode(new SetModeRequest { Mode = (TherapyMode)(byte)SelectedMode });
                await RecordCommandAsync("设置模式", $"模式:{SelectedMode}");
                await _communicationService.SendCommandAsync(ProtocolCommandCode.SetMode, modeData);
                await Task.Delay(50);

                // 发送强度设置命令
                var intensityData = CommandBuilder.BuildSetIntensity(new SetIntensityRequest { Intensity = (byte)Intensity });
                await RecordCommandAsync("设置强度", $"强度:{Intensity:F0}%");
                await _communicationService.SendCommandAsync(ProtocolCommandCode.SetIntensity, intensityData);
                await Task.Delay(50);

                // 发送时长设置命令
                var durationData = CommandBuilder.BuildSetDuration(new SetDurationRequest { Duration = (byte)Duration });
                await RecordCommandAsync("设置时长", $"时长:{Duration}分钟");
                await _communicationService.SendCommandAsync(ProtocolCommandCode.SetDuration, durationData);
                await Task.Delay(50);

                // 发送步进电机转速命令
                var stepperData = CommandBuilder.BuildStepperControl(new StepperControlRequest
                {
                    MotorId = 1,
                    Speed = (ushort)StepperSpeed,
                    Direction = 0
                });
                await RecordCommandAsync("步进电机控制", $"电机:1, 转速:{StepperSpeed:F0}rpm, 方向:0");
                await _communicationService.SendCommandAsync(ProtocolCommandCode.StepperControl, stepperData);

                // 发送舵机角度命令
                foreach (var servo in ServoAngles)
                {
                    var servoData = CommandBuilder.BuildServoControl(new ServoControlRequest
                    {
                        ServoId = (byte)servo.ServoId,
                        Angle = (byte)servo.Angle
                    });
                    await RecordCommandAsync("舵机控制", $"舵机:{servo.ServoId}, 角度:{servo.Angle}°");
                    await _communicationService.SendCommandAsync(ProtocolCommandCode.ServoControl, servoData);
                    await Task.Delay(20);
                }

                CommandResult = $"参数已保存并下发 - 模式:{SelectedMode}, 强度:{Intensity}%, 时长:{Duration}分钟, 转速:{StepperSpeed}rpm";
            }
            catch (Exception ex)
            {
                CommandResult = $"参数下发失败: {ex.Message}";
            }
        }

        private async Task HomeAllServosAsync()
        {
            try
            {
                // 发送所有舵机归位命令
                await RecordCommandAsync("舵机归位", "所有舵机回到90°");
                await _communicationService.SendCommandAsync(ProtocolCommandCode.ServoHomeAll, null);

                // 更新本地角度值
                foreach (var servo in ServoAngles)
                {
                    servo.Angle = 90;
                }

                CommandResult = "所有舵机已归位";
            }
            catch (Exception ex)
            {
                CommandResult = $"舵机归位失败: {ex.Message}";
            }
        }

        private void OnSessionUpdated(object? sender, TreatmentSession session)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsTreatmentRunning = _deviceTherapyActive || session.IsRunning;
                if (!session.IsRunning && session.Progress >= 100)
                {
                    CommandResult = "治疗已完成！";
                }
                // 额外调用一次确保刷新
                RefreshCommandStates();
                CommandManager.InvalidateRequerySuggested();
            });

            if (!session.IsRunning && session.Progress >= 100)
            {
                _ = PersistSessionEndAsync(session);
            }
        }

        private async Task PersistSessionEndAsync(TreatmentSession session)
        {
            try
            {
                await _treatmentRepository.EndTreatmentSessionAsync(session.SessionId, session.EndTime ?? DateTime.Now);
                _sessionPersisted = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动保存治疗会话结束记录失败。");
            }
        }

        private async Task RecordCommandAsync(string commandName, string commandDetails)
        {
            var commandLog = new CommandLog
            {
                Timestamp = DateTime.Now,
                CommandName = commandName,
                CommandDetails = commandDetails
            };

            if (_sessionPersisted)
            {
                var sessionId = _treatmentEngine.CurrentSession.SessionId;
                if (sessionId != Guid.Empty)
                {
                    try
                    {
                        await _treatmentRepository.AddCommandLogsAsync(sessionId, new[] { commandLog });
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "保存控制指令失败，已转为临时缓存。");
                    }
                }
            }

            lock (_pendingCommandLogs)
            {
                _pendingCommandLogs.Add(commandLog);
            }
        }

        private async Task FlushPendingCommandLogsAsync(Guid sessionId)
        {
            List<CommandLog> pendingLogs;

            lock (_pendingCommandLogs)
            {
                if (_pendingCommandLogs.Count == 0)
                {
                    return;
                }

                pendingLogs = new List<CommandLog>(_pendingCommandLogs);
                _pendingCommandLogs.Clear();
            }

            foreach (var log in pendingLogs)
            {
                log.SessionId = sessionId;
            }

            try
            {
                await _treatmentRepository.AddCommandLogsAsync(sessionId, pendingLogs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新待保存控制指令失败，已重新放回缓存。");
                lock (_pendingCommandLogs)
                {
                    _pendingCommandLogs.AddRange(pendingLogs);
                }
            }
        }

        public void Dispose()
        {
            _treatmentEngine.SessionUpdated -= OnSessionUpdated;
            _communicationService.SystemStatusChanged -= OnSystemStatusChanged;
            _communicationService.PressureDataReceived -= OnPressureDataReceived;
            _communicationService.EdemaDataReceived -= OnEdemaDataReceived;
            _communicationService.MotorStatusReceived -= OnMotorStatusReceived;
            _communicationService.TherapyProgressReceived -= OnTherapyProgressReceived;
        }
    }

    public class ServoAngleItem : ObservableObject
    {
        private double _angle;

        public int ServoId { get; set; }

        public double Angle
        {
            get => _angle;
            set => SetField(ref _angle, value);
        }

        public string DisplayName => $"舵机 {ServoId}";
    }
}
