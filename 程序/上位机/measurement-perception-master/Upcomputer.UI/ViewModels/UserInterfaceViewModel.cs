using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Upcomputer.Common.Models;
using Upcomputer.Core.Models;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Services;
using Upcomputer.Data.Database;
using System.Windows.Input;

namespace Upcomputer.UI.ViewModels
{
    public class UserInterfaceViewModel : ObservableObject
    {
        // ===== 水肿测试数据（调试用，可修改数值测试不同场景） =====
        private const double TestEdemaPercent = 180.0; // 0-100 的水肿百分比

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IUserSelected _userSelected;
        private readonly ITreatmentRepository _treatmentRepository;
        private readonly TreatmentEngine _treatmentEngine;
        private readonly ControlPanelViewModel _controlPanelViewModel;
        private readonly DashboardViewModel _dashboardViewModel;
        private readonly ICommunicationService _communicationService;
        private readonly ObservableCollection<UserProfile> _users = new();
        private readonly ObservableCollection<UserProfile> _filteredUsers = new();
        private UserProfile? _selectedUser;
        private string _statusText = "正在从数据库加载用户...";
        private bool _isConfirmed;
        private string _searchText = string.Empty;
        private Task? _loadUsersTask;
        private readonly ObservableCollection<CalendarDayCell> _calendarDays = new();
        private string _calendarMonthTitle = string.Empty;
        private double _latestEdemaPercent;
        private bool _hasLatestEdemaData;
        private string _doctorAdvice = "暂无医生建议。";
        private string _rehabilitationReminder = "暂无待办提醒。";

        // 水肿预测相关属性
        private EdemaPredictionResult? _edemaPredictionResult;
        public EdemaPredictionResult? EdemaPredictionResult
        {
            get => _edemaPredictionResult;
            set => SetField(ref _edemaPredictionResult, value);
        }

        public UserInterfaceViewModel(
            IDbContextFactory<AppDbContext> contextFactory,
            IUserSelected userSelected,
            ITreatmentRepository treatmentRepository,
            TreatmentEngine treatmentEngine,
            ControlPanelViewModel controlPanelViewModel,
            DashboardViewModel dashboardViewModel,
            ICommunicationService communicationService)
        {
            _contextFactory = contextFactory;
            _userSelected = userSelected;
            _treatmentRepository = treatmentRepository;
            _treatmentEngine = treatmentEngine;
            _controlPanelViewModel = controlPanelViewModel;
            _dashboardViewModel = dashboardViewModel;
            _communicationService = communicationService;
            EnterCommand = new RelayCommand(OnEnter);
            ExitCommand = new RelayCommand(OnExit);
            SearchCommand = new RelayCommand(OnSearch);
            PredictEdemaCommand = new RelayCommand(OnPredictEdema);
            TestEdemaCommand = new RelayCommand(OnTestEdema);
            _ = RefreshUsersAsync();
            _communicationService.EdemaDataReceived += OnEdemaDataReceived;

            _dashboardViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DashboardViewModel.CurrentSession))
                {
                    OnPropertyChanged(nameof(CurrentSession));
                }
            };

            _controlPanelViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ControlPanelViewModel.IsTreatmentRunning)
                    || e.PropertyName == nameof(ControlPanelViewModel.IsConnected)
                    || e.PropertyName == nameof(ControlPanelViewModel.CanStartTreatment)
                    || e.PropertyName == nameof(ControlPanelViewModel.CanStopTreatment))
                {
                    OnPropertyChanged(nameof(CanStartTreatment));
                    OnPropertyChanged(nameof(CanStopTreatment));
                }
            };
        }

        public Task RefreshUsersAsync()
        {
            // Always start a fresh reload when explicitly requested by navigation.
            // This avoids stale state if previous loads completed quickly and ensures
            // newest DB changes are reflected immediately.
            _loadUsersTask = LoadUsersAsync();
            return _loadUsersTask;
        }

        public ObservableCollection<UserProfile> Users => _users;

        public ObservableCollection<UserProfile> FilteredUsers => _filteredUsers;

        public ObservableCollection<CalendarDayCell> CalendarDays => _calendarDays;

        public string CalendarMonthTitle
        {
            get => _calendarMonthTitle;
            private set => SetField(ref _calendarMonthTitle, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetField(ref _searchText, value ?? string.Empty))
                {
                    ApplyUserFilter();
                    OnPropertyChanged(nameof(SelectedUserDisplayText));
                }
            }
        }

        public string SelectedUserDisplayText
            => SelectedUser?.UserName
               ?? (!string.IsNullOrWhiteSpace(SearchText) && FilteredUsers.Count == 0 ? "未找到匹配用户" : "请选择用户");

        public UserProfile? SelectedUser
        {
            get => _selectedUser;
            set
            {
                if (SetField(ref _selectedUser, value) && value != null)
                {
                    StatusText = $"当前选择：{value.UserName}";
                    _ = RefreshCalendarAsync();
                }
                else if (value == null)
                {
                    ClearCalendar();
                }

                OnPropertyChanged(nameof(SelectedUserDisplayText));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public bool IsConfirmed
        {
            get => _isConfirmed;
            private set => SetField(ref _isConfirmed, value);
        }

        public ICommand EnterCommand { get; }

        public ICommand ExitCommand { get; }

        public ICommand SearchCommand { get; }
        public ICommand PredictEdemaCommand { get; }
        public ICommand TestEdemaCommand { get; }

        public ICommand StartTreatmentCommand => _controlPanelViewModel.StartCommand;

        public ICommand StopTreatmentCommand => _controlPanelViewModel.StopCommand;

        public ObservableCollection<PressureData> PressureDataList => _dashboardViewModel.PressureDataList;

        public TreatmentSession CurrentSession => _dashboardViewModel.CurrentSession;

        public bool CanStartTreatment => _controlPanelViewModel.CanStartTreatment;

        public bool CanStopTreatment => _controlPanelViewModel.CanStopTreatment;

        public string DoctorAdvice
        {
            get => _doctorAdvice;
            private set => SetField(ref _doctorAdvice, value);
        }

        public string RehabilitationReminder
        {
            get => _rehabilitationReminder;
            private set => SetField(ref _rehabilitationReminder, value);
        }

        /// <summary>
        /// 重置回用户选择页面（用户手动点击"退出"按钮时调用）
        /// </summary>
        public void ResetToSelection()
        {
            if (IsConfirmed)
            {
                IsConfirmed = false;
                StatusText = "请重新选择用户。";
            }
        }

        /// <summary>
        /// 验证当前已确认的用户是否仍存在于数据库中
        /// 不存在则自动退出回到选择页面；存在则保持当前状态不变
        /// </summary>
        public async Task ValidateCurrentSelectionAsync()
        {
            if (!IsConfirmed || SelectedUser == null) return;

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var stillExists = await context.UserProfiles
                    .AsNoTracking()
                    .AnyAsync(u => u.UserId == SelectedUser.UserId);

                if (!stillExists)
                {
                    IsConfirmed = false;
                    StatusText = "当前用户已被删除，请重新选择用户。";
                }
            }
            catch (Exception)
            {
                // 数据库访问失败时不重置，保持当前状态
            }
        }

        private void OnEnter()
        {
            if (SelectedUser == null)
            {
                StatusText = "请先选择一个用户再进入。";
                return;
            }

            _userSelected.Publish(SelectedUser);
            IsConfirmed = true;
            StatusText = $"已选择：{SelectedUser.UserName}";
        }

        private void OnExit()
        {
            if (_treatmentEngine.CurrentSession is { IsRunning: true })
            {
                var result = MessageBox.Show(
                    "当前治疗正在进行，是否结束治疗并退出？",
                    "确认退出",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                _treatmentEngine.StopTreatment();
            }

            IsConfirmed = false;
            StatusText = "已退出，请重新选择用户。";
        }

        private void OnSearch()
        {
            ApplyUserFilter();
        }

        private async Task RefreshCalendarAsync()
        {
            var selectedUser = SelectedUser;
            if (selectedUser == null)
            {
                ClearCalendar();
                return;
            }

            var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var monthEnd = monthStart.AddMonths(1);

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var recordDates = await context.TreatmentSessions
                    .AsNoTracking()
                    .Where(x => x.UserId == selectedUser.UserId && x.StartTime >= monthStart && x.StartTime < monthEnd)
                    .Select(x => x.StartTime.Date)
                    .Distinct()
                    .ToListAsync();

                var recordedDays = recordDates.ToHashSet();
                var daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
                var cells = new List<CalendarDayCell>(daysInMonth);

                for (var day = 1; day <= daysInMonth; day++)
                {
                    var date = new DateTime(monthStart.Year, monthStart.Month, day);
                    var status = date.Date > DateTime.Today
                        ? CalendarDayStatus.Future
                        : recordedDays.Contains(date)
                            ? CalendarDayStatus.Completed
                            : CalendarDayStatus.Missed;
                    cells.Add(new CalendarDayCell(day, date, status));
                }

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    CalendarMonthTitle = monthStart.ToString("yyyy年MM月");
                    _calendarDays.Clear();
                    foreach (var cell in cells)
                    {
                        _calendarDays.Add(cell);
                    }
                });

                await LoadAdviceAndReminderAsync();
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    StatusText = $"日历加载失败：{ex.Message}";
                });
            }
        }

        private void ClearCalendar()
        {
            var now = DateTime.Now;
            CalendarMonthTitle = now.ToString("yyyy年MM月");

            _calendarDays.Clear();
            var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            for (var day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(now.Year, now.Month, day);
                var status = date.Date > DateTime.Today ? CalendarDayStatus.Future : CalendarDayStatus.Missed;
                _calendarDays.Add(new CalendarDayCell(day, date, status));
            }
        }

        private async Task LoadAdviceAndReminderAsync()
        {
            try
            {
                var advice = await _treatmentRepository.GetDoctorAdviceAsync();
                var reminders = await _treatmentRepository.GetPendingRemindersAsync();

                DoctorAdvice = string.IsNullOrWhiteSpace(advice) ? "暂无医生建议。" : advice;
                RehabilitationReminder = reminders.Count == 0
                    ? "暂无待办提醒。"
                    : string.Join(Environment.NewLine, reminders.Select(x => $"• {x.Content}（{x.DueAt:MM-dd HH:mm}）"));
            }
            catch (Exception)
            {
                DoctorAdvice = "暂无医生建议。";
                RehabilitationReminder = "暂无待办提醒。";
            }
        }

        private async Task LoadUsersAsync()
        {
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var users = await context.UserProfiles
                    .AsNoTracking()
                    .OrderBy(x => x.UserName)
                    .ToListAsync();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _users.Clear();
                    foreach (var user in users)
                    {
                        _users.Add(user);
                    }

                    ApplyUserFilter();
                    SelectedUser ??= _filteredUsers.FirstOrDefault();
                    if (SelectedUser == null)
                    {
                        StatusText = "未查询到用户，请先初始化数据库。";
                        ClearCalendar();
                    }
                    else
                    {
                        _ = RefreshCalendarAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    StatusText = $"用户加载失败：{ex.Message}";
                });
            }
            finally
            {
                _loadUsersTask = null;
            }
        }

        private void ApplyUserFilter()
        {
            var query = (SearchText ?? string.Empty).Trim();
            var snapshot = _users.ToArray();

            IEnumerable<UserProfile> results;
            if (string.IsNullOrWhiteSpace(query))
            {
                results = snapshot.OrderBy(x => x.UserName);
            }
            else
            {
                results = snapshot
                    .Select(u => new { User = u, Score = CalculateUserNameScore(u.UserName, query) })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.User.UserName)
                    .Select(x => x.User);
            }

            void UpdateCollection()
            {
                // 保存当前选中的用户 ID，在 Clear 之前。
                // 因为 _filteredUsers.Clear() 会触发 ComboBox TwoWay 绑定，
                // 自动将 SelectedUser 置为 null，导致后续无法恢复选中状态。
                var selectedUserId = SelectedUser?.UserId;

                _filteredUsers.Clear();
                foreach (var user in results)
                {
                    _filteredUsers.Add(user);
                }

                // 保留当前选中的用户，仅当当前选中不在新列表中时才重置
                // 注意：每次从数据库重新加载后是新实例，不能用 Contains（引用相等），需按 UserId 比较
                // 找到后要将 SelectedUser 指向新实例，否则 ComboBox 绑定找不到集合中的对应项
                if (selectedUserId != null)
                {
                    var matched = _filteredUsers.FirstOrDefault(u => u.UserId == selectedUserId);
                    if (matched != null)
                    {
                        // 更新引用到新实例，确保 ComboBox 绑定正确
                        SelectedUser = matched;
                    }
                    else if (_filteredUsers.Count > 0)
                    {
                        SelectedUser = _filteredUsers[0];
                    }
                    else
                    {
                        SelectedUser = null;
                        if (!string.IsNullOrWhiteSpace(query))
                        {
                            StatusText = "未找到匹配用户。";
                        }
                    }
                }
                else if (_filteredUsers.Count > 0)
                {
                    SelectedUser = _filteredUsers[0];
                }
                else
                {
                    SelectedUser = null;
                    if (!string.IsNullOrWhiteSpace(query))
                    {
                        StatusText = "未找到匹配用户。";
                    }
                }

                OnPropertyChanged(nameof(SelectedUserDisplayText));
            }

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                UpdateCollection();
            }
            else
            {
                Application.Current?.Dispatcher?.Invoke(UpdateCollection);
            }
        }

        private static int CalculateUserNameScore(string? userName, string query)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(query))
            {
                return 0;
            }

            var name = userName.Trim();
            var q = query.Trim();

            var nameLower = name.ToLowerInvariant();
            var qLower = q.ToLowerInvariant();

            if (nameLower == qLower) return 1000;

            if (nameLower.StartsWith(qLower))
            {
                return 900 - Math.Min(100, Math.Max(0, name.Length - q.Length));
            }

            var idx = nameLower.IndexOf(qLower, StringComparison.Ordinal);
            if (idx >= 0)
            {
                return 700 - Math.Min(200, idx);
            }

            // subsequence fuzzy match: query characters appear in order
            var score = 0;
            var lastPos = -1;
            foreach (var ch in qLower)
            {
                var pos = nameLower.IndexOf(ch, lastPos + 1);
                if (pos < 0) return 0;

                var gap = pos - lastPos - 1;
                score += Math.Max(1, 15 - gap);
                lastPos = pos;
            }

            return 300 + Math.Min(200, score);
        }

        private void OnPredictEdema()
        {
            if (SelectedUser == null)
            {
                StatusText = "请先选择用户";
                return;
            }

            if (!_hasLatestEdemaData)
            {
                StatusText = "暂无实时水肿数据，请先采集后再预测。";
                return;
            }

            // 下位机上报的是百分比（如 150），模型输入 r 为比值（如 1.5），除以 100 转换
            double rRatio = NormalizeRatio(_latestEdemaPercent);
            if (!TryBuildPredictFeatures(SelectedUser, out var age, out var sex, out var bmi, out var validationMessage))
            {
                StatusText = validationMessage;
                return;
            }

            var modelPath = ResolveEdemaModelPath();
            if (!File.Exists(modelPath))
            {
                StatusText = $"模型参数文件不存在: {modelPath}";
                return;
            }
            var predictor = new EdemaPredictor(modelPath);
            EdemaPredictionResult = predictor.Predict(age, sex, bmi, rRatio);
            StatusText = $"水肿风险: {EdemaPredictionResult.Severity}, {EdemaPredictionResult.SeverityDescription}";
        }

        private void OnTestEdema()
        {
            _latestEdemaPercent = TestEdemaPercent;
            _hasLatestEdemaData = true;
            TryAutoPredictEdema();
            StatusText = $"[测试] 水肿百分比: {TestEdemaPercent}%，预测完成";
        }

        private static bool TryBuildPredictFeatures(UserProfile user, out double age, out int sex, out double bmi, out string validationMessage)
        {
            age = 0;
            sex = 0;
            bmi = 0;
            validationMessage = string.Empty;

            if (user.BirthDate is not DateTime birthDate)
            {
                validationMessage = "当前用户缺少出生日期，无法预测。";
                return false;
            }

            if (birthDate > DateTime.Today)
            {
                validationMessage = "当前用户出生日期异常，无法预测。";
                return false;
            }

            age = CalculateAge(birthDate, DateTime.Today);
            if (age <= 0 || age > 120)
            {
                validationMessage = "当前用户年龄数据异常，无法预测。";
                return false;
            }

            if (user.Bmi is not double userBmi || userBmi <= 0)
            {
                validationMessage = "当前用户BMI缺失或无效，无法预测。";
                return false;
            }

            bmi = userBmi;

            var gender = (user.Gender ?? string.Empty).Trim();
            if (gender is "男" or "male" or "Male" or "M" or "m")
            {
                sex = 1;
                return true;
            }

            if (gender is "女" or "female" or "Female" or "F" or "f")
            {
                sex = 0;
                return true;
            }

            validationMessage = "当前用户性别缺失或不支持，无法预测。";
            return false;
        }

        private static int CalculateAge(DateTime birthDate, DateTime today)
        {
            var age = today.Year - birthDate.Year;
            if (birthDate.Date > today.AddYears(-age))
            {
                age--;
            }

            return age;
        }

        private void OnEdemaDataReceived(object? sender, EdemaDataReport report)
        {
            _latestEdemaPercent = report.EdemaPercent;
            _hasLatestEdemaData = true;

            // 有数据就自动预测
            TryAutoPredictEdema();
        }

        private void TryAutoPredictEdema()
        {
            if (SelectedUser == null || !_hasLatestEdemaData)
                return;

            double rRatio = NormalizeRatio(_latestEdemaPercent);
            if (!TryBuildPredictFeatures(SelectedUser, out var age, out var sex, out var bmi, out _))
                return;

            var modelPath = ResolveEdemaModelPath();
            if (!File.Exists(modelPath))
                return;

            var predictor = new EdemaPredictor(modelPath);
            EdemaPredictionResult = predictor.Predict(age, sex, bmi, rRatio);
            StatusText = $"水肿风险: {EdemaPredictionResult.Severity}, {EdemaPredictionResult.SeverityDescription}";
        }

        private static double NormalizeRatio(double edemaPercent)
        {
            // 模型训练时 r 的均值约 1.54，范围可大于 1，不做 Clamp 截断
            return edemaPercent > 1 ? edemaPercent / 100.0 : edemaPercent;
        }

        private static string ResolveEdemaModelPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "ModelParams", "edema_model_params.json"),
                Path.Combine(baseDir, "..", "ModelParams", "edema_model_params.json"),
                Path.Combine(baseDir, "..", "..", "..", "..", "Upcomputer.Core", "ModelParams", "edema_model_params.json"),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "Upcomputer.Core", "ModelParams", "edema_model_params.json")
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return Path.GetFullPath(candidates[0]);
        }

        public sealed record CalendarDayCell(int Day, DateTime Date, CalendarDayStatus Status)
        {
            public string DayText => Day.ToString();
            public string TooltipText => $"{Date:yyyy-MM-dd} {StatusText}";
            public string StatusText => Status switch
            {
                CalendarDayStatus.Completed => "已按摩",
                CalendarDayStatus.Missed => "未按摩",
                _ => "未到日期"
            };
        }

        public enum CalendarDayStatus
        {
            Completed,
            Missed,
            Future
        }
    }
}
