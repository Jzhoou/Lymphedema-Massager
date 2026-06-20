using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Upcomputer.Common.Models;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;

namespace Upcomputer.UI.ViewModels
{
    public class HistoryViewModel : ObservableObject
    {
        private readonly ITreatmentRepository _treatmentRepository;
        private readonly ILogger<HistoryViewModel> _logger;
        private static readonly double[] PressureFusionWeights = { 1.0, 1.1, 1.2, 1.2, 1.1, 1.0 };
        private const int DetailLogPageSize = 20;
        private const int DetailCurveSampleStride = 3;
        private const int MaxDetailCurvePoints = 180;
        private const int MaxPdfTableRows = 2000;
        private const int MaxPdfCurvePoints = 1200;

        private DateTime _startDate = DateTime.Now.AddDays(-7);
        private DateTime _endDate = DateTime.Now;
        private ObservableCollection<TreatmentRecord> _treatmentRecords = new();
        private ObservableCollection<HistoryCurvePoint> _selectedSessionCurve = new();
        private ObservableCollection<HistoryCurvePoint> _selectedPressureCurve = new();
        private ObservableCollection<HistoryCurvePoint> _selectedEdemaCurve = new();
        private ObservableCollection<PressureChartPoint> _selectedPressureChartHistory = new();
        private ObservableCollection<PressureLogItem> _pressureLogItems = new();
        private ObservableCollection<EdemaLogItem> _edemaLogItems = new();
        private ObservableCollection<CommandLogItem> _commandLogItems = new();
        private ObservableCollection<ColumnOption> _pressureColumnOptions;
        private ObservableCollection<ColumnOption> _edemaColumnOptions;
        private ObservableCollection<ColumnOption> _commandColumnOptions;
        private TreatmentRecord? _selectedRecord;
        private string _doctorAdvice = string.Empty;
        private string _rehabilitationReminder = string.Empty;
        private string _statusMessage = "就绪";
        private bool _isLoading;
        private bool _isDetailViewVisible;
        private string _selectedSessionSummary = string.Empty;
        private readonly ObservableCollection<string> _detailChartModes = new() { "水肿百分比", "压力数据" };
        private string _selectedDetailChartMode = "水肿百分比";
        private string _selectedDetailYAxisTitle = "水肿百分比 (%)";
        private string _selectedDetailSeriesLabel = "水肿百分比";
        private int _currentPage = 1;
        private readonly int _pageSize = 20;
        private int _totalItems;
        private Guid _selectedSessionId;
        private int _pressureDetailPageIndex = 1;
        private int _edemaDetailPageIndex = 1;
        private int _commandDetailPageIndex = 1;
        private int _pressureDetailTotalItems;
        private int _edemaDetailTotalItems;
        private int _commandDetailTotalItems;
        private bool _hasMorePressureLogs;
        private bool _hasMoreEdemaLogs;
        private bool _hasMoreCommandLogs;
        private bool _isDetailLoading;
        private bool _isPressureLogsLoadingMore;
        private bool _isEdemaLogsLoadingMore;
        private bool _isCommandLogsLoadingMore;

        public HistoryViewModel(ITreatmentRepository treatmentRepository, ILogger<HistoryViewModel> logger)
        {
            _treatmentRepository = treatmentRepository;
            _logger = logger;

            SearchCommand = new AsyncRelayCommand(LoadFirstPageAsync);
            ExportCommand = new AsyncRelayCommand(ExportDataAsync);
            DetailExportCommand = new AsyncRelayCommand(ExportDetailDataAsync, () => SelectedRecord != null && !IsLoading && !IsDetailLoading);
            DetailReportExportCommand = new AsyncRelayCommand(ExportDetailReportAsync, () => SelectedRecord != null && !IsLoading && !IsDetailLoading);
            DeleteCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync, () => SelectedRecord != null && !IsLoading && !IsDetailLoading);
            RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
            BackCommand = new RelayCommand(GoBackToList);
            PreviousPageCommand = new AsyncRelayCommand(LoadPreviousPageAsync, () => CurrentPage > 1 && !IsLoading);
            NextPageCommand = new AsyncRelayCommand(LoadNextPageAsync, () => CurrentPage < TotalPages && !IsLoading);

            _ = LoadDataAsync();

            // Initialize column selection options (default checked)
            _pressureColumnOptions = new ObservableCollection<ColumnOption>
            {
                new ColumnOption("时间", true),
                new ColumnOption("传感器", true),
                new ColumnOption("当前值", true),
                new ColumnOption("最大值", true),
                new ColumnOption("阈值", true),
                new ColumnOption("告警", true),
                new ColumnOption("延迟(ms)", true)
            };

            _edemaColumnOptions = new ObservableCollection<ColumnOption>
            {
                new ColumnOption("时间", true),
                new ColumnOption("传感器", true),
                new ColumnOption("阻抗", true),
                new ColumnOption("水肿百分比", true),
                new ColumnOption("阈值", true),
                new ColumnOption("告警", true),
                new ColumnOption("延迟(ms)", true)
            };

            _commandColumnOptions = new ObservableCollection<ColumnOption>
            {
                new ColumnOption("时间", true),
                new ColumnOption("指令", true),
                new ColumnOption("详情", true)
            };
        }

    public class ColumnOption : ObservableObject
    {
        private string _name = string.Empty;
        private bool _isChecked;

        public ColumnOption(string name, bool isChecked)
        {
            _name = name;
            _isChecked = isChecked;
        }

        public string Name { get => _name; set => SetField(ref _name, value); }
        public bool IsChecked { get => _isChecked; set => SetField(ref _isChecked, value); }
    }

        public DateTime StartDate
        {
            get => _startDate;
            set => SetField(ref _startDate, value);
        }

        public DateTime EndDate
        {
            get => _endDate;
            set => SetField(ref _endDate, value);
        }

        public ObservableCollection<TreatmentRecord> TreatmentRecords
        {
            get => _treatmentRecords;
            set => SetField(ref _treatmentRecords, value);
        }

        public ObservableCollection<HistoryCurvePoint> SelectedSessionCurve
        {
            get => _selectedSessionCurve;
            set => SetField(ref _selectedSessionCurve, value);
        }

        public ObservableCollection<HistoryCurvePoint> SelectedPressureCurve
        {
            get => _selectedPressureCurve;
            set => SetField(ref _selectedPressureCurve, value);
        }

        public ObservableCollection<HistoryCurvePoint> SelectedEdemaCurve
        {
            get => _selectedEdemaCurve;
            set => SetField(ref _selectedEdemaCurve, value);
        }

        public ObservableCollection<PressureChartPoint> SelectedPressureChartHistory
        {
            get => _selectedPressureChartHistory;
            set => SetField(ref _selectedPressureChartHistory, value);
        }

        public ObservableCollection<PressureLogItem> PressureLogItems
        {
            get => _pressureLogItems;
            set => SetField(ref _pressureLogItems, value);
        }

        public ObservableCollection<ColumnOption> PressureColumnOptions
        {
            get => _pressureColumnOptions;
            set => SetField(ref _pressureColumnOptions, value);
        }

        public ObservableCollection<EdemaLogItem> EdemaLogItems
        {
            get => _edemaLogItems;
            set => SetField(ref _edemaLogItems, value);
        }

        public ObservableCollection<ColumnOption> EdemaColumnOptions
        {
            get => _edemaColumnOptions;
            set => SetField(ref _edemaColumnOptions, value);
        }

        public ObservableCollection<CommandLogItem> CommandLogItems
        {
            get => _commandLogItems;
            set => SetField(ref _commandLogItems, value);
        }

        public ObservableCollection<ColumnOption> CommandColumnOptions
        {
            get => _commandColumnOptions;
            set => SetField(ref _commandColumnOptions, value);
        }

        public TreatmentRecord? SelectedRecord
        {
            get => _selectedRecord;
            set
            {
                if (SetField(ref _selectedRecord, value))
                {
                    (DetailExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (DetailReportExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (DeleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

                    if (value == null)
                    {
                        ResetDetailState();
                    }
                }
            }
        }

        public bool IsPressureLogsLoadingMore
        {
            get => _isPressureLogsLoadingMore;
            set => SetField(ref _isPressureLogsLoadingMore, value);
        }

        public bool IsEdemaLogsLoadingMore
        {
            get => _isEdemaLogsLoadingMore;
            set => SetField(ref _isEdemaLogsLoadingMore, value);
        }

        public bool IsCommandLogsLoadingMore
        {
            get => _isCommandLogsLoadingMore;
            set => SetField(ref _isCommandLogsLoadingMore, value);
        }

        public bool IsDetailViewVisible
        {
            get => _isDetailViewVisible;
            set => SetField(ref _isDetailViewVisible, value);
        }

        public string SelectedSessionSummary
        {
            get => _selectedSessionSummary;
            set => SetField(ref _selectedSessionSummary, value);
        }

        public ObservableCollection<string> DetailChartModes => _detailChartModes;

        public string SelectedDetailChartMode
        {
            get => _selectedDetailChartMode;
            set
            {
                if (SetField(ref _selectedDetailChartMode, value))
                {
                    UpdateSelectedDetailChartSource();
                }
            }
        }

        public string SelectedDetailYAxisTitle
        {
            get => _selectedDetailYAxisTitle;
            set => SetField(ref _selectedDetailYAxisTitle, value);
        }

        public string SelectedDetailSeriesLabel
        {
            get => _selectedDetailSeriesLabel;
            set => SetField(ref _selectedDetailSeriesLabel, value);
        }

        public bool IsDetailLoading
        {
            get => _isDetailLoading;
            set
            {
                if (SetField(ref _isDetailLoading, value))
                {
                    (DetailExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (DetailReportExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (DeleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string DoctorAdvice
        {
            get => _doctorAdvice;
            set => SetField(ref _doctorAdvice, value);
        }

        public string RehabilitationReminder
        {
            get => _rehabilitationReminder;
            set => SetField(ref _rehabilitationReminder, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetField(ref _isLoading, value))
                {
                    (PreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (NextPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (DetailExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (DetailReportExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (DeleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetField(ref _currentPage, value))
                {
                    OnPropertyChanged(nameof(TotalPages));
                    OnPropertyChanged(nameof(CanGoPreviousPage));
                    OnPropertyChanged(nameof(CanGoNextPage));
                    (PreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (NextPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public int PageSize => _pageSize;

        public int TotalItems
        {
            get => _totalItems;
            set
            {
                if (SetField(ref _totalItems, value))
                {
                    OnPropertyChanged(nameof(TotalPages));
                    OnPropertyChanged(nameof(CanGoPreviousPage));
                    OnPropertyChanged(nameof(CanGoNextPage));
                    (PreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                    (NextPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalItems / PageSize));

        public bool CanGoPreviousPage => CurrentPage > 1;
        public bool CanGoNextPage => CurrentPage < TotalPages;

        public ICommand SearchCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand DetailExportCommand { get; }
        public ICommand DetailReportExportCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }

        public Task OpenSessionDetailAsync(Guid sessionId)
        {
            return LoadSessionDetailsAsync(sessionId);
        }

        public Task LoadMorePressureLogsAsync()
        {
            return LoadMoreDetailLogsAsync(true);
        }

        public Task LoadMoreEdemaLogsAsync()
        {
            return LoadMoreDetailLogsAsync(false);
        }

        public Task LoadMoreCommandLogsAsync()
        {
            return LoadMoreCommandLogsInternalAsync();
        }

        private async Task LoadDataAsync()
        {
            IsLoading = true;
            StatusMessage = "正在加载历史数据...";

            try
            {
                var (queryStart, queryEnd) = GetSearchRange();

                TotalItems = await _treatmentRepository.GetSessionCountAsync(queryStart, queryEnd);
                CurrentPage = Math.Min(CurrentPage, TotalPages);

                var sessions = await _treatmentRepository.GetSessionsAsync(queryStart, queryEnd, CurrentPage, PageSize);

                TreatmentRecords = new ObservableCollection<TreatmentRecord>(sessions.Select(MapSession));
                SelectedRecord = null;
                ResetDetailState();
                SelectedDetailChartMode = "水肿百分比";
                (DetailExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (DetailReportExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

                await LoadDoctorAdviceAsync();
                await LoadRemindersAsync();

                StatusMessage = $"已加载 {TreatmentRecords.Count} 条记录，共 {TotalItems} 条。";
            }
            catch (SqliteException ex)
            {
                _logger.LogError(ex, "加载历史数据失败。");
                StatusMessage = "数据库连接失败，已切换降级模式。";
                MessageBox.Show("数据库连接失败，应用已切换到降级模式。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "加载历史数据失败。");
                StatusMessage = "加载历史数据失败。";
                MessageBox.Show($"加载历史数据失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                (PreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (NextPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        private async Task DeleteSelectedSessionAsync()
        {
            if (SelectedRecord == null)
            {
                return;
            }

            var result = MessageBox.Show(
                $"确认删除会话 {SelectedRecord.StartTimeDisplay} 吗？\n删除后将同时移除该会话的压力、水肿和控制指令详情。",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var deleted = await _treatmentRepository.DeleteTreatmentSessionAsync(SelectedRecord.SessionId);
                if (!deleted)
                {
                    MessageBox.Show("未找到要删除的会话。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                StatusMessage = "会话已删除。";
                await LoadDataAsync();
            }
            catch (Exception ex) when (ex is InvalidOperationException or SqliteException)
            {
                _logger.LogError(ex, "删除会话失败。");
                MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadPreviousPageAsync()
        {
            if (CurrentPage <= 1)
            {
                return;
            }

            CurrentPage--;
            await LoadDataAsync();
        }

        private async Task LoadNextPageAsync()
        {
            if (CurrentPage >= TotalPages)
            {
                return;
            }

            CurrentPage++;
            await LoadDataAsync();
        }

        private async Task LoadFirstPageAsync()
        {
            CurrentPage = 1;
            await LoadDataAsync();
        }

        private async Task LoadSessionDetailsAsync(Guid sessionId)
        {
            IsDetailLoading = true;
            StatusMessage = "正在加载会话详情...";

            try
            {
                ResetDetailState();
                _selectedSessionId = sessionId;

                var sessionTask = _treatmentRepository.GetSessionDetailsAsync(sessionId);
                var pressureCountTask = _treatmentRepository.GetPressureLogCountAsync(sessionId);
                var edemaCountTask = _treatmentRepository.GetEdemaLogCountAsync(sessionId);
                var commandCountTask = _treatmentRepository.GetCommandLogCountAsync(sessionId);

                await Task.WhenAll(sessionTask, pressureCountTask, edemaCountTask, commandCountTask);

                var session = await sessionTask;
                if (session == null)
                {
                    StatusMessage = "未找到对应的会话详情。";
                    return;
                }

                SelectedSessionSummary = $"会话时间: {session.StartTime:yyyy-MM-dd HH:mm}  |  模式: {session.ModeDisplay}  |  时长: {session.TotalDuration / 60} 分钟  |  强度: {session.Intensity:F0}%  |  状态: {(session.IsRunning ? "进行中" : "已完成")}";

                _pressureDetailTotalItems = pressureCountTask.Result;
                _edemaDetailTotalItems = edemaCountTask.Result;
                _commandDetailTotalItems = commandCountTask.Result;

                var pressureLogs = await _treatmentRepository.GetPressureLogsAsync(sessionId, 1, DetailLogPageSize);
                var edemaLogs = await _treatmentRepository.GetEdemaLogsAsync(sessionId, 1, DetailLogPageSize);
                var commandLogs = await _treatmentRepository.GetCommandLogsAsync(sessionId, 1, DetailLogPageSize);

                AppendPressureLogs(pressureLogs);
                AppendEdemaLogs(edemaLogs);
                AppendCommandLogs(commandLogs);
                AppendPressureCurves(pressureLogs);
                AppendEdemaCurves(edemaLogs);

                _pressureDetailPageIndex = pressureLogs.Count > 0 ? 2 : 1;
                _edemaDetailPageIndex = edemaLogs.Count > 0 ? 2 : 1;
                _commandDetailPageIndex = commandLogs.Count > 0 ? 2 : 1;
                _hasMorePressureLogs = PressureLogItems.Count < _pressureDetailTotalItems;
                _hasMoreEdemaLogs = EdemaLogItems.Count < _edemaDetailTotalItems;
                _hasMoreCommandLogs = CommandLogItems.Count < _commandDetailTotalItems;

                SelectedDetailChartMode = "水肿百分比";
                IsDetailViewVisible = true;

                if (_edemaDetailTotalItems == 0)
                {
                    StatusMessage = "当前会话暂无水肿数据。";
                }
                else if (PressureLogItems.Count == 0)
                {
                    StatusMessage = "当前会话暂无压力数据。";
                }
                else
                {
                    StatusMessage = $"已加载压力 {PressureLogItems.Count}/{_pressureDetailTotalItems} 条，水肿 {EdemaLogItems.Count}/{_edemaDetailTotalItems} 条，指令 {CommandLogItems.Count}/{_commandDetailTotalItems} 条。";
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or SqliteException)
            {
                _logger.LogError(ex, "加载会话详情失败。");
                StatusMessage = "加载会话详情失败。";
                IsDetailViewVisible = false;
            }
            finally
            {
                IsDetailLoading = false;
            }
        }

        private async Task LoadMoreCommandLogsInternalAsync()
        {
            if (IsDetailLoading || _selectedSessionId == Guid.Empty || !_hasMoreCommandLogs)
            {
                return;
            }

            IsDetailLoading = true;
            IsCommandLogsLoadingMore = true;

            try
            {
                var logs = await _treatmentRepository.GetCommandLogsAsync(_selectedSessionId, _commandDetailPageIndex, DetailLogPageSize);
                if (logs.Count == 0)
                {
                    _hasMoreCommandLogs = false;
                    return;
                }

                AppendCommandLogs(logs);
                _commandDetailPageIndex++;
                _hasMoreCommandLogs = CommandLogItems.Count < _commandDetailTotalItems;
                StatusMessage = $"已加载控制指令 {CommandLogItems.Count}/{_commandDetailTotalItems} 条。";
            }
            catch (Exception ex) when (ex is InvalidOperationException or SqliteException)
            {
                _logger.LogError(ex, "加载控制指令分页数据失败。");
                StatusMessage = "加载更多控制指令失败。";
            }
            finally
            {
                IsCommandLogsLoadingMore = false;
                IsDetailLoading = false;
            }
        }

        private async Task LoadMoreDetailLogsAsync(bool isPressure)
        {
            if (IsDetailLoading || _selectedSessionId == Guid.Empty)
            {
                return;
            }

            if (isPressure ? !_hasMorePressureLogs : !_hasMoreEdemaLogs)
            {
                return;
            }

            IsDetailLoading = true;

            try
            {
                if (isPressure)
                {
                    IsPressureLogsLoadingMore = true;
                    var logs = await _treatmentRepository.GetPressureLogsAsync(_selectedSessionId, _pressureDetailPageIndex, DetailLogPageSize);
                    if (logs.Count == 0)
                    {
                        _hasMorePressureLogs = false;
                        return;
                    }

                    AppendPressureLogs(logs);
                    AppendPressureCurves(logs);
                    _pressureDetailPageIndex++;
                    _hasMorePressureLogs = PressureLogItems.Count < _pressureDetailTotalItems;
                    StatusMessage = $"已加载压力 {PressureLogItems.Count}/{_pressureDetailTotalItems} 条。";
                }
                else
                {
                    IsEdemaLogsLoadingMore = true;
                    var logs = await _treatmentRepository.GetEdemaLogsAsync(_selectedSessionId, _edemaDetailPageIndex, DetailLogPageSize);
                    if (logs.Count == 0)
                    {
                        _hasMoreEdemaLogs = false;
                        return;
                    }

                    AppendEdemaLogs(logs);
                    AppendEdemaCurves(logs);
                    _edemaDetailPageIndex++;
                    _hasMoreEdemaLogs = EdemaLogItems.Count < _edemaDetailTotalItems;
                    StatusMessage = $"已加载水肿 {EdemaLogItems.Count}/{_edemaDetailTotalItems} 条。";
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or SqliteException)
            {
                _logger.LogError(ex, "加载会话详情分页数据失败。");
                StatusMessage = "加载更多详情数据失败。";
            }
            finally
            {
                IsPressureLogsLoadingMore = false;
                IsEdemaLogsLoadingMore = false;
                IsDetailLoading = false;
            }
        }

        private async Task LoadDoctorAdviceAsync()
        {
            var advice = await _treatmentRepository.GetDoctorAdviceAsync();
            DoctorAdvice = string.IsNullOrWhiteSpace(advice)
                ? "暂无医生建议。"
                : advice;
        }

        private async Task LoadRemindersAsync()
        {
            var reminders = await _treatmentRepository.GetPendingRemindersAsync();
            RehabilitationReminder = reminders.Count == 0
                ? "暂无待办提醒。"
                : string.Join(Environment.NewLine, reminders.Select(x => $"• {x.Content}（{x.DueAt:MM-dd HH:mm}）"));
        }

        private void GoBackToList()
        {
            SelectedRecord = null;
            ResetDetailState();
            SelectedDetailChartMode = "水肿百分比";
            StatusMessage = "已返回会话列表。";
            (DetailExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (DetailReportExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        private void UpdateSelectedDetailChartSource()
        {
            if (SelectedDetailChartMode == "压力数据")
            {
                SelectedSessionCurve = SelectedPressureCurve;
                SelectedDetailYAxisTitle = "压力值 (g)";
                SelectedDetailSeriesLabel = "压力值";
            }
            else
            {
                SelectedSessionCurve = SelectedEdemaCurve;
                SelectedDetailYAxisTitle = "水肿百分比 (%)";
                SelectedDetailSeriesLabel = "水肿百分比";
            }
        }

        private void ResetDetailState()
        {
            _selectedSessionId = Guid.Empty;
            _pressureDetailPageIndex = 1;
            _edemaDetailPageIndex = 1;
            _pressureDetailTotalItems = 0;
            _edemaDetailTotalItems = 0;
            _hasMorePressureLogs = false;
            _hasMoreEdemaLogs = false;
            _hasMoreCommandLogs = false;

            IsPressureLogsLoadingMore = false;
            IsEdemaLogsLoadingMore = false;
            IsCommandLogsLoadingMore = false;

            PressureLogItems = new ObservableCollection<PressureLogItem>();
            EdemaLogItems = new ObservableCollection<EdemaLogItem>();
            CommandLogItems = new ObservableCollection<CommandLogItem>();
            SelectedPressureCurve = new ObservableCollection<HistoryCurvePoint>();
            SelectedEdemaCurve = new ObservableCollection<HistoryCurvePoint>();
            SelectedPressureChartHistory = new ObservableCollection<PressureChartPoint>();
            SelectedSessionCurve = new ObservableCollection<HistoryCurvePoint>();
            SelectedSessionSummary = string.Empty;
            IsDetailViewVisible = false;
        }

        private void AppendPressureLogs(IEnumerable<PressureData> logs)
        {
            foreach (var log in logs)
            {
                PressureLogItems.Add(new PressureLogItem(log.Timestamp, log.SensorId, log.CurrentValue, log.MaxValue, log.Threshold, log.IsAlert, log.LatencyMs));
            }
        }

        private void AppendEdemaLogs(IEnumerable<EdemaData> logs)
        {
            foreach (var log in logs)
            {
                EdemaLogItems.Add(new EdemaLogItem(log.Timestamp, log.SensorId, log.Impedance, log.EdemaPercentage, log.Threshold, log.IsAlert, log.LatencyMs));
            }
        }

        private void AppendCommandLogs(IEnumerable<CommandLog> logs)
        {
            foreach (var log in logs)
            {
                CommandLogItems.Add(new CommandLogItem(log.Timestamp, log.CommandName, log.CommandDetails));
            }
        }

        private void AppendPressureCurves(IEnumerable<PressureData> logs)
        {
            var pressurePoints = BuildPressureChartHistory(logs);
            var sampledPressurePoints = pressurePoints
                .Where((_, index) => index % DetailCurveSampleStride == 0)
                .ToList();

            foreach (var point in sampledPressurePoints)
            {
                SelectedPressureChartHistory.Add(point);
                SelectedPressureCurve.Add(new HistoryCurvePoint(point.Time, point.FusedPressure));
            }

            TrimCurvePoints(SelectedPressureChartHistory);
            TrimCurvePoints(SelectedPressureCurve);

            if (SelectedDetailChartMode == "压力数据")
            {
                SelectedSessionCurve = SelectedPressureCurve;
            }
        }

        private void AppendEdemaCurves(IEnumerable<EdemaData> logs)
        {
            var points = logs
                .OrderBy(x => x.Timestamp)
                .Select(x => new HistoryCurvePoint(x.Timestamp, x.EdemaPercentage))
                .Where((_, index) => index % DetailCurveSampleStride == 0)
                .ToList();

            foreach (var point in points)
            {
                SelectedEdemaCurve.Add(point);
            }

            TrimCurvePoints(SelectedEdemaCurve);

            if (SelectedDetailChartMode != "压力数据")
            {
                SelectedSessionCurve = SelectedEdemaCurve;
            }
        }

        private static void TrimCurvePoints<T>(ObservableCollection<T> points)
        {
            while (points.Count > MaxDetailCurvePoints)
            {
                points.RemoveAt(0);
            }
        }

        private async Task ExportDetailDataAsync()
        {
            if (SelectedRecord == null)
            {
                MessageBox.Show("没有可导出的会话详情。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|CSV 文件 (*.csv)|*.csv",
                FileName = $"会话详情_{SelectedRecord.StartTime:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var detailExport = await BuildDetailExportDataAsync();
                var selectedCommandColumns = GetSelectedColumnNames(CommandColumnOptions);
                var selectedPressureColumns = GetSelectedColumnNames(PressureColumnOptions);
                var selectedEdemaColumns = GetSelectedColumnNames(EdemaColumnOptions);

                if (System.IO.Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    await File.WriteAllTextAsync(dialog.FileName, BuildDetailCsv(detailExport, selectedCommandColumns, selectedPressureColumns, selectedEdemaColumns), Encoding.UTF8);
                }
                else
                {
                    await WriteDetailXlsxAsync(dialog.FileName, detailExport, selectedCommandColumns, selectedPressureColumns, selectedEdemaColumns);
                }

                StatusMessage = $"已导出会话详情到 {dialog.FileName}";
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "导出会话详情失败。");
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "导出会话详情失败。");
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static IReadOnlyList<PressureChartPoint> BuildPressureChartHistory(IEnumerable<PressureData>? pressureLogs)
        {
            if (pressureLogs == null)
            {
                return Array.Empty<PressureChartPoint>();
            }

            var orderedLogs = pressureLogs
                .OrderBy(x => x.Timestamp)
                .ThenBy(x => x.SensorId)
                .ToList();

            if (orderedLogs.Count == 0)
            {
                return Array.Empty<PressureChartPoint>();
            }

            var points = new List<PressureChartPoint>();

            for (var i = 0; i < orderedLogs.Count; i += 6)
            {
                var batch = orderedLogs.Skip(i).Take(6).ToList();
                if (batch.Count == 0)
                {
                    continue;
                }

                points.Add(CreatePressureChartPoint(batch));
            }

            return points;
        }

        private static PressureChartPoint CreatePressureChartPoint(IReadOnlyList<PressureData> batch)
        {
            var values = new double[6];

            foreach (var log in batch)
            {
                var index = log.SensorId - 1;
                if (index >= 0 && index < values.Length)
                {
                    values[index] = log.CurrentValue;
                }
            }

            return new PressureChartPoint
            {
                Time = batch[0].Timestamp,
                Sensor1 = values[0],
                Sensor2 = values[1],
                Sensor3 = values[2],
                Sensor4 = values[3],
                Sensor5 = values[4],
                Sensor6 = values[5],
                FusedPressure = CalculateWeightedAverage(values)
            };
        }

        private static double CalculateWeightedAverage(IReadOnlyList<double> values)
        {
            var count = Math.Min(values.Count, PressureFusionWeights.Length);
            if (count == 0)
            {
                return 0;
            }

            var weightedSum = 0d;
            var totalWeight = 0d;

            for (var i = 0; i < count; i++)
            {
                weightedSum += values[i] * PressureFusionWeights[i];
                totalWeight += PressureFusionWeights[i];
            }

            return totalWeight > 0 ? weightedSum / totalWeight : 0;
        }

        private async Task ExportDataAsync()
        {
            if (TreatmentRecords.Count == 0)
            {
                MessageBox.Show("没有可导出的数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|CSV 文件 (*.csv)|*.csv",
                FileName = $"治疗记录_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var (queryStart, queryEnd) = GetSearchRange();

                var exportRows = (await _treatmentRepository.GetSessionsAsync(queryStart, queryEnd, 1, Math.Max(TotalItems, PageSize)))
                    .Select(MapSession)
                    .ToList();

                if (System.IO.Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    await File.WriteAllTextAsync(dialog.FileName, BuildCsv(exportRows), Encoding.UTF8);
                }
                else
                {
                    await WriteXlsxAsync(dialog.FileName, exportRows);
                }

                StatusMessage = $"已导出到 {dialog.FileName}";
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "导出失败。");
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "导出失败。");
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static TreatmentRecord MapSession(TreatmentSession session)
        {
            return new TreatmentRecord
            {
                SessionId = session.SessionId,
                StartTime = session.StartTime,
                UserName = session.User?.UserName ?? session.UserId,
                Mode = session.ModeDisplay,
                Duration = $"{session.TotalDuration / 60}分钟",
                Intensity = $"{session.Intensity:F0}%",
                CompletionRate = session.IsRunning ? $"{session.Progress:F0}%" : (session.EndTime.HasValue ? "100%" : $"{session.Progress:F0}%"),
                Status = session.IsRunning ? "进行中" : "已完成"
            };
        }

        private static string BuildCsv(IEnumerable<TreatmentRecord> records)
        {
            var builder = new StringBuilder();
            builder.AppendLine("时间,用户名,模式,时长,强度,完成率,状态");

            foreach (var record in records)
            {
                builder.AppendLine(string.Join(",",
                    EscapeCsv(record.StartTimeDisplay),
                    EscapeCsv(record.UserName),
                    EscapeCsv(record.Mode),
                    EscapeCsv(record.Duration),
                    EscapeCsv(record.Intensity),
                    EscapeCsv(record.CompletionRate),
                    EscapeCsv(record.Status)));
            }

            return builder.ToString();
        }

        private async Task<DetailExportData> BuildDetailExportDataAsync()
        {
            if (SelectedRecord == null)
            {
                return new DetailExportData
                {
                    Summary = SelectedSessionSummary,
                    PressureLogs = Array.Empty<PressureLogItem>(),
                    EdemaLogs = Array.Empty<EdemaLogItem>(),
                    CommandLogs = Array.Empty<CommandLogItem>()
                };
            }

            var sessionId = SelectedRecord.SessionId;
            var pressureCount = await _treatmentRepository.GetPressureLogCountAsync(sessionId);
            var edemaCount = await _treatmentRepository.GetEdemaLogCountAsync(sessionId);
            var commandCount = await _treatmentRepository.GetCommandLogCountAsync(sessionId);

            var pressureLogs = pressureCount == 0
                ? new List<PressureData>()
                : await _treatmentRepository.GetPressureLogsAsync(sessionId, 1, pressureCount);
            var edemaLogs = edemaCount == 0
                ? new List<EdemaData>()
                : await _treatmentRepository.GetEdemaLogsAsync(sessionId, 1, edemaCount);
            var commandLogs = commandCount == 0
                ? new List<CommandLogItem>()
                : (await _treatmentRepository.GetCommandLogsAsync(sessionId, 1, commandCount))
                    .Select(x => new CommandLogItem(x.Timestamp, x.CommandName, x.CommandDetails))
                    .ToList();

            return new DetailExportData
            {
                Summary = SelectedSessionSummary,
                PressureLogs = pressureLogs
                    .Select(log => new PressureLogItem(log.Timestamp, log.SensorId, log.CurrentValue, log.MaxValue, log.Threshold, log.IsAlert, log.LatencyMs))
                    .ToList(),
                EdemaLogs = edemaLogs
                    .Select(log => new EdemaLogItem(log.Timestamp, log.SensorId, log.Impedance, log.EdemaPercentage, log.Threshold, log.IsAlert, log.LatencyMs))
                    .ToList(),
                CommandLogs = commandLogs
            };
        }

        private static string BuildDetailCsv(
            DetailExportData data,
            IReadOnlyList<string> selectedCommandColumns,
            IReadOnlyList<string> selectedPressureColumns,
            IReadOnlyList<string> selectedEdemaColumns)
        {
            var builder = new StringBuilder();
            builder.AppendLine("会话详情");
            builder.AppendLine(EscapeCsv(data.Summary));
            builder.AppendLine();
            builder.AppendLine("控制指令");
            AppendCsvHeader(builder, selectedCommandColumns);

            foreach (var log in data.CommandLogs)
            {
                AppendCsvRow(builder, BuildCommandColumnValues(log, selectedCommandColumns));
            }

            builder.AppendLine();
            builder.AppendLine("压力日志");
            AppendCsvHeader(builder, selectedPressureColumns);

            foreach (var log in data.PressureLogs)
            {
                AppendCsvRow(builder, BuildPressureColumnValues(log, selectedPressureColumns));
            }

            builder.AppendLine();
            builder.AppendLine("水肿日志");
            AppendCsvHeader(builder, selectedEdemaColumns);

            foreach (var log in data.EdemaLogs)
            {
                AppendCsvRow(builder, BuildEdemaColumnValues(log, selectedEdemaColumns));
            }

            return builder.ToString();
        }

        private static void AppendCsvHeader(StringBuilder builder, IReadOnlyList<string> headers)
        {
            builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        }

        private static void AppendCsvRow(StringBuilder builder, IReadOnlyList<string> values)
        {
            builder.AppendLine(string.Join(",", values.Select(EscapeCsv)));
        }

        private static string EscapeCsv(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }

        private static Task WriteXlsxAsync(string filePath, IReadOnlyList<TreatmentRecord> records)
        {
            using var stream = File.Create(filePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

            WriteEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            WriteEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            WriteEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"治疗记录\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");

            var sheetBuilder = new StringBuilder();
            sheetBuilder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            WriteRow(sheetBuilder, 1, "时间", "用户名", "模式", "时长", "强度", "完成率", "状态");

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                WriteRow(sheetBuilder, i + 2, record.StartTimeDisplay, record.UserName, record.Mode, record.Duration, record.Intensity, record.CompletionRate, record.Status);
            }

            sheetBuilder.AppendLine("</sheetData></worksheet>");
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheetBuilder.ToString());

            return Task.CompletedTask;
        }

        private static Task WriteDetailXlsxAsync(
            string filePath,
            DetailExportData data,
            IReadOnlyList<string> selectedCommandColumns,
            IReadOnlyList<string> selectedPressureColumns,
            IReadOnlyList<string> selectedEdemaColumns)
        {
            using var stream = File.Create(filePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

            WriteEntry(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            WriteEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            WriteEntry(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"会话详情\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");

            var sheetBuilder = new StringBuilder();
            sheetBuilder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            WriteRow(sheetBuilder, 1, "会话详情");
            WriteRow(sheetBuilder, 2, data.Summary);
            WriteRow(sheetBuilder, 4, "控制指令");
            WriteRow(sheetBuilder, 5, selectedCommandColumns.ToArray());

            var row = 6;
            foreach (var log in data.CommandLogs)
            {
                WriteRow(sheetBuilder, row++, BuildCommandColumnValues(log, selectedCommandColumns).ToArray());
            }

            row += 1;
            WriteRow(sheetBuilder, row++, "压力日志");
            WriteRow(sheetBuilder, row++, selectedPressureColumns.ToArray());

            foreach (var log in data.PressureLogs)
            {
                WriteRow(sheetBuilder, row++, BuildPressureColumnValues(log, selectedPressureColumns).ToArray());
            }

            row += 1;
            WriteRow(sheetBuilder, row++, "水肿日志");
            WriteRow(sheetBuilder, row++, selectedEdemaColumns.ToArray());

            foreach (var log in data.EdemaLogs)
            {
                WriteRow(sheetBuilder, row++, BuildEdemaColumnValues(log, selectedEdemaColumns).ToArray());
            }

            sheetBuilder.AppendLine("</sheetData></worksheet>");
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheetBuilder.ToString());

            return Task.CompletedTask;
        }

        private static void WriteEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        private static void WriteRow(StringBuilder builder, int rowNumber, params string[] values)
        {
            builder.Append($"<row r=\"{rowNumber}\">");

            for (var i = 0; i < values.Length; i++)
            {
                var cellRef = $"{GetColumnName(i + 1)}{rowNumber}";
                builder.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{SecurityElementEscape(values[i])}</t></is></c>");
            }

            builder.AppendLine("</row>");
        }

        private static string GetColumnName(int columnNumber)
        {
            var dividend = columnNumber;
            var columnName = string.Empty;

            while (dividend > 0)
            {
                var modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar(65 + modulo) + columnName;
                dividend = (dividend - modulo) / 26;
            }

            return columnName;
        }

        private static string SecurityElementEscape(string value)
        {
            return System.Security.SecurityElement.Escape(value) ?? string.Empty;
        }

        private static IReadOnlyList<string> GetSelectedColumnNames(ObservableCollection<ColumnOption> columns)
        {
            var selected = columns.Where(column => column.IsChecked).Select(column => column.Name).ToList();
            return selected.Count == 0 ? columns.Select(column => column.Name).ToList() : selected;
        }

        private static IReadOnlyList<string> BuildPressureColumnValues(PressureLogItem log, IReadOnlyList<string> selectedColumns)
        {
            return selectedColumns.Select(name => name switch
            {
                "时间" => log.TimeDisplay,
                "传感器" => log.SensorId.ToString(),
                "当前值" => log.ValueDisplay,
                "最大值" => log.MaxDisplay,
                "告警" => log.IsAlert ? "是" : "否",
                "延迟(ms)" => log.LatencyDisplay,
                "当前" => log.ValueDisplay,
                "阈值" => log.ThresholdDisplay,
                "状态" => log.IsAlert ? "是" : "否",
                _ => string.Empty
            }).ToList();
        }

        private static IReadOnlyList<string> BuildCommandColumnValues(CommandLogItem log, IReadOnlyList<string> selectedColumns)
        {
            return selectedColumns.Select(name => name switch
            {
                "时间" => log.TimeDisplay,
                "指令" => log.CommandName,
                "详情" => log.CommandDetails,
                _ => string.Empty
            }).ToList();
        }

        private static IReadOnlyList<string> BuildEdemaColumnValues(EdemaLogItem log, IReadOnlyList<string> selectedColumns)
        {
            return selectedColumns.Select(name => name switch
            {
                "时间" => log.TimeDisplay,
                "传感器" => log.SensorId.ToString(),
                "阻抗" => log.ImpedanceDisplay,
                "水肿百分比" => log.PercentageDisplay,
                "延迟(ms)" => log.LatencyDisplay,
                "水肿" => log.PercentageDisplay,
                "阈值" => log.ThresholdDisplay,
                "告警" => log.IsAlert ? "是" : "否",
                _ => string.Empty
            }).ToList();
        }

        private async Task ExportDetailReportAsync()
        {
            if (SelectedRecord == null)
            {
                MessageBox.Show("没有可导出的会话报表。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var userName = SanitizeFileNamePart(SelectedRecord.UserName);
            var dialog = new SaveFileDialog
            {
                Filter = "PDF 文件 (*.pdf)|*.pdf",
                FileName = $"{userName}_{SelectedRecord.StartTime:yyyyMMdd_HHmmss}.pdf"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                StatusMessage = "正在生成PDF报表，请稍候...";

                var detailExport = await BuildDetailExportDataAsync();
                var sessionId = SelectedRecord.SessionId;
                var pressureCount = await _treatmentRepository.GetPressureLogCountAsync(sessionId);
                var edemaCount = await _treatmentRepository.GetEdemaLogCountAsync(sessionId);
                var pressureLogs = pressureCount == 0
                    ? new List<PressureData>()
                    : await _treatmentRepository.GetPressureLogsAsync(sessionId, 1, pressureCount);
                var edemaLogs = edemaCount == 0
                    ? new List<EdemaData>()
                    : await _treatmentRepository.GetEdemaLogsAsync(sessionId, 1, edemaCount);

                var pressureCurve = BuildPressureChartHistory(pressureLogs)
                    .Where((_, index) => index % DetailCurveSampleStride == 0)
                    .Select(point => new HistoryCurvePoint(point.Time, point.FusedPressure))
                    .ToList();
                var edemaCurve = edemaLogs
                    .OrderBy(log => log.Timestamp)
                    .Where((_, index) => index % DetailCurveSampleStride == 0)
                    .Select(log => new HistoryCurvePoint(log.Timestamp, log.EdemaPercentage))
                    .ToList();

                var selectedPressureColumns = GetSelectedColumnNames(PressureColumnOptions);
                var selectedEdemaColumns = GetSelectedColumnNames(EdemaColumnOptions);
                var selectedCommandColumns = GetSelectedColumnNames(CommandColumnOptions);

                var pressureLogsForPdf = DownsampleForPdf(detailExport.PressureLogs, MaxPdfTableRows);
                var edemaLogsForPdf = DownsampleForPdf(detailExport.EdemaLogs, MaxPdfTableRows);
                var commandLogsForPdf = DownsampleForPdf(detailExport.CommandLogs, MaxPdfTableRows);
                var pressureCurveForPdf = DownsampleForPdf(pressureCurve, MaxPdfCurvePoints);
                var edemaCurveForPdf = DownsampleForPdf(edemaCurve, MaxPdfCurvePoints);

                var notes = new List<string>();
                if (detailExport.PressureLogs.Count > pressureLogsForPdf.Count)
                {
                    notes.Add($"压力日志过多，报表仅抽样展示 {pressureLogsForPdf.Count}/{detailExport.PressureLogs.Count} 条。");
                }

                if (detailExport.EdemaLogs.Count > edemaLogsForPdf.Count)
                {
                    notes.Add($"水肿日志过多，报表仅抽样展示 {edemaLogsForPdf.Count}/{detailExport.EdemaLogs.Count} 条。");
                }

                if (detailExport.CommandLogs.Count > commandLogsForPdf.Count)
                {
                    notes.Add($"控制指令过多，报表仅抽样展示 {commandLogsForPdf.Count}/{detailExport.CommandLogs.Count} 条。");
                }

                var summary = detailExport.Summary;
                if (notes.Count > 0)
                {
                    summary += Environment.NewLine + string.Join(Environment.NewLine, notes);
                }

                var detailExportForPdf = new DetailExportData
                {
                    Summary = summary,
                    PressureLogs = pressureLogsForPdf,
                    EdemaLogs = edemaLogsForPdf,
                    CommandLogs = commandLogsForPdf
                };

                // Prevent concurrent PDF generation to avoid iText errors and ensure WPF rendering
                // occurs in a dedicated STA thread. Acquire semaphore to serialize PDF export operations.
                await _pdfGenerationSemaphore.WaitAsync();
                try
                {
                    var session = await _treatmentRepository.GetSessionDetailsAsync(sessionId);
                    var user = session?.User;

                    await RunOnStaThreadAsync(() => WriteDetailPdfReport(
                        dialog.FileName,
                        SelectedRecord,
                        detailExportForPdf,
                        edemaCurveForPdf,
                        pressureCurveForPdf,
                        selectedPressureColumns,
                        selectedEdemaColumns,
                        selectedCommandColumns,
                        user));
                }
                finally
                {
                    _pdfGenerationSemaphore.Release();
                }

                StatusMessage = $"已导出会话报表到 {dialog.FileName}";
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "导出会话报表失败。");
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "导出会话报表失败。");
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出会话报表失败。");
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void WriteDetailPdfReport(
            string filePath,
            TreatmentRecord selectedRecord,
            DetailExportData data,
            IReadOnlyList<HistoryCurvePoint> edemaCurve,
            IReadOnlyList<HistoryCurvePoint> pressureCurve,
            IReadOnlyList<string> pressureColumns,
            IReadOnlyList<string> edemaColumns,
            IReadOnlyList<string> commandColumns,
            UserProfile? user)
        {
            using var writer = new PdfWriter(filePath);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4.Rotate());

            var font = CreateReportFont();
            document.SetFont(font);
            document.SetFontSize(10);

            document.Add(new Paragraph("治疗会话报表")
                .SetFontSize(16)
                .SetBold()
                .SetMarginBottom(8));

            document.Add(new Paragraph("一、用户信息与会话详情").SetBold().SetFontSize(12));
            document.Add(new Paragraph($"用户：{selectedRecord.UserName}"));
            if (user != null)
            {
                document.Add(new Paragraph($"性别：{user.Gender ?? "-"}  身高：{(user.HeightCm.HasValue ? user.HeightCm.Value.ToString("F0") + " cm" : "-")}  体重：{(user.WeightKg.HasValue ? user.WeightKg.Value.ToString("F0") + " kg" : "-")}  BMI：{(user.Bmi.HasValue ? user.Bmi.Value.ToString("F1") : "-")}"));
            }
            document.Add(new Paragraph($"会话时间：{selectedRecord.StartTimeDisplay}"));
            document.Add(new Paragraph($"会话详情：{data.Summary}").SetMarginBottom(10));

            document.Add(new Paragraph("二、会话曲线").SetBold().SetFontSize(12));
            AddCurveImage(document, "水肿百分比曲线", edemaCurve, "水肿百分比 (%)");
            AddCurveImage(document, "压力数据曲线", pressureCurve, "压力值 (g)");

            document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            document.Add(new Paragraph("三、压力日志和水肿日志").SetBold().SetFontSize(12).SetMarginBottom(8));

            document.Add(new Paragraph("控制指令").SetBold());
            document.Add(BuildCommandTable(data.CommandLogs, commandColumns));

            document.Add(new Paragraph("压力日志").SetBold());
            document.Add(BuildPressureTable(data.PressureLogs, pressureColumns));

            document.Add(new Paragraph(" "));
            document.Add(new Paragraph("水肿日志").SetBold());
            document.Add(BuildEdemaTable(data.EdemaLogs, edemaColumns));
        }

        // Ensure only one PDF generation runs at a time to avoid iText "PdfPages tree could be generated only once" errors
        private static readonly System.Threading.SemaphoreSlim _pdfGenerationSemaphore = new(1, 1);

        private static PdfFont CreateReportFont()
        {
            // Create a fresh PdfFont for each PDF generation to avoid reusing iText objects
            // across multiple PdfDocument lifecycles which can cause "PdfPages tree could be generated only once".
            var fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var candidates = new[]
            {
                System.IO.Path.Combine(fontDir, "msyh.ttc"),
                System.IO.Path.Combine(fontDir, "simhei.ttf"),
                System.IO.Path.Combine(fontDir, "simsun.ttc")
            };

            var existing = candidates.FirstOrDefault(System.IO.File.Exists);

            if (existing == null)
            {
                return PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            }

            if (existing.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
            {
                var ttc = new TrueTypeCollection(existing);
                var fontProgram = ttc.GetFontByTccIndex(0);
                return PdfFontFactory.CreateFont(fontProgram, PdfEncodings.IDENTITY_H);
            }

            return PdfFontFactory.CreateFont(existing, PdfEncodings.IDENTITY_H);
        }

        private static void AddCurveImage(Document document, string title, IReadOnlyList<HistoryCurvePoint> points, string yAxisTitle)
        {
            document.Add(new Paragraph(title).SetBold().SetMarginTop(6).SetMarginBottom(4));

            if (points.Count == 0)
            {
                document.Add(new Paragraph("暂无曲线数据。").SetFontColor(ColorConstants.GRAY));
                return;
            }

            var imageBytes = BuildCurveImage(points, yAxisTitle);
            // Ensure the generated chart image fills the available width consistently.
            // Disable AutoScale to make SetWidth effective and avoid inconsistent sizing
            // between different charts (e.g. 压力 vs 水肿).
            var image = new Image(ImageDataFactory.Create(imageBytes))
                .SetMarginBottom(8)
                .SetWidth(UnitValue.CreatePercentValue(100))
                .SetAutoScale(false);

            document.Add(image);
        }

        private static byte[] BuildCurveImage(IReadOnlyList<HistoryCurvePoint> points, string yAxisTitle)
        {
            const int width = 1200;
            const int height = 320;
            const double left = 70;
            const double right = 30;
            const double top = 35;
            const double bottom = 45;

            var minY = points.Min(x => x.Value);
            var maxY = points.Max(x => x.Value);
            if (Math.Abs(maxY - minY) < 0.0001)
            {
                maxY += 1;
                minY -= 1;
            }

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

                var axisPen = new Pen(Brushes.DimGray, 1);
                dc.DrawLine(axisPen, new System.Windows.Point(left, top), new System.Windows.Point(left, height - bottom));
                dc.DrawLine(axisPen, new System.Windows.Point(left, height - bottom), new System.Windows.Point(width - right, height - bottom));

                var chartWidth = width - left - right;
                var chartHeight = height - top - bottom;
                var valueRange = maxY - minY;

                var linePen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 150, 220)), 2);
                System.Windows.Point? previous = null;

                for (var i = 0; i < points.Count; i++)
                {
                    var x = left + (points.Count == 1 ? 0 : chartWidth * i / (points.Count - 1d));
                    var y = top + (maxY - points[i].Value) / valueRange * chartHeight;
                    var current = new System.Windows.Point(x, y);

                    if (previous.HasValue)
                    {
                        dc.DrawLine(linePen, previous.Value, current);
                    }

                    previous = current;
                }

                var typeface = new Typeface("Microsoft YaHei");
                var dpi = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
                var textBrush = Brushes.Black;

                var yTitle = new FormattedText(yAxisTitle, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 12, textBrush, dpi);
                dc.DrawText(yTitle, new System.Windows.Point(8, 8));

                var startLabel = new FormattedText(points.First().TimeDisplay, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, textBrush, dpi);
                var endLabel = new FormattedText(points.Last().TimeDisplay, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, textBrush, dpi);
                dc.DrawText(startLabel, new System.Windows.Point(left, height - bottom + 6));
                dc.DrawText(endLabel, new System.Windows.Point(width - right - endLabel.Width, height - bottom + 6));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private static Table BuildPressureTable(IReadOnlyList<PressureLogItem> logs)
        {
            var table = new Table(UnitValue.CreatePercentArray(new float[] { 2f, 1f, 1.4f, 1.4f, 1.4f, 1f, 1f }))
                .UseAllAvailableWidth();
            AddTableHeader(table, "时间", "传感器", "当前值", "最大值", "阈值", "告警", "延迟(ms)");

            foreach (var log in logs)
            {
                AddTableCells(table,
                    log.TimeDisplay,
                    log.SensorId.ToString(),
                    log.ValueDisplay,
                    log.MaxDisplay,
                    log.ThresholdDisplay,
                    log.IsAlert ? "是" : "否",
                    log.LatencyDisplay);
            }

            return table;
        }

        private static Table BuildEdemaTable(IReadOnlyList<EdemaLogItem> logs)
        {
            var table = new Table(UnitValue.CreatePercentArray(new float[] { 2f, 1f, 1.4f, 1.4f, 1.4f, 1f, 1f }))
                .UseAllAvailableWidth();
            AddTableHeader(table, "时间", "传感器", "阻抗", "水肿百分比", "阈值", "告警", "延迟(ms)");

            foreach (var log in logs)
            {
                AddTableCells(table,
                    log.TimeDisplay,
                    log.SensorId.ToString(),
                    log.ImpedanceDisplay,
                    log.PercentageDisplay,
                    log.ThresholdDisplay,
                    log.IsAlert ? "是" : "否",
                    log.LatencyDisplay);
            }

            return table;
        }

        // New overloads that respect column selection
        private static Table BuildPressureTable(IReadOnlyList<PressureLogItem> logs, IReadOnlyList<string> columns)
        {
            // Map selected columns to headers
            var selected = columns;
            var widths = selected.Select(_ => 1f).ToArray();
            var table = new Table(UnitValue.CreatePercentArray(widths)).UseAllAvailableWidth();

            AddTableHeader(table, selected.ToArray());

            foreach (var log in logs)
            {
                var cells = new List<string>();
                foreach (var name in selected)
                {
                    cells.Add(name switch
                    {
                        "时间" => log.TimeDisplay,
                        "传感器" => log.SensorId.ToString(),
                        "当前值" => log.ValueDisplay.Replace("kPa", "g"),
                        "最大值" => log.MaxDisplay.Replace("kPa", "g"),
                        "阈值" => log.ThresholdDisplay.Replace("kPa", "g"),
                        "告警" => log.IsAlert ? "是" : "否",
                        "延迟(ms)" => log.LatencyDisplay,
                        _ => string.Empty
                    });
                }

                AddTableCells(table, cells.ToArray());
            }

            return table;
        }

        private static Table BuildEdemaTable(IReadOnlyList<EdemaLogItem> logs, IReadOnlyList<string> columns)
        {
            var selected = columns;
            var widths = selected.Select(_ => 1f).ToArray();
            var table = new Table(UnitValue.CreatePercentArray(widths)).UseAllAvailableWidth();

            AddTableHeader(table, selected.ToArray());

            foreach (var log in logs)
            {
                var cells = new List<string>();
                foreach (var name in selected)
                {
                    cells.Add(name switch
                    {
                        "时间" => log.TimeDisplay,
                        "传感器" => log.SensorId.ToString(),
                        "阻抗" => log.ImpedanceDisplay,
                        "水肿百分比" => log.PercentageDisplay,
                        "阈值" => log.ThresholdDisplay,
                        "告警" => log.IsAlert ? "是" : "否",
                        "延迟(ms)" => log.LatencyDisplay,
                        _ => string.Empty
                    });
                }

                AddTableCells(table, cells.ToArray());
            }

            return table;
        }

        private static Table BuildCommandTable(IReadOnlyList<CommandLogItem> logs, IReadOnlyList<string> columns)
        {
            var selected = columns;
            var widths = selected.Select(_ => 1f).ToArray();
            var table = new Table(UnitValue.CreatePercentArray(widths)).UseAllAvailableWidth();

            AddTableHeader(table, selected.ToArray());

            foreach (var log in logs)
            {
                var cells = new List<string>();
                foreach (var name in selected)
                {
                    cells.Add(name switch
                    {
                        "时间" => log.TimeDisplay,
                        "指令" => log.CommandName,
                        "详情" => log.CommandDetails,
                        _ => string.Empty
                    });
                }

                AddTableCells(table, cells.ToArray());
            }

            return table;
        }

        private static IReadOnlyList<T> DownsampleForPdf<T>(IReadOnlyList<T> items, int maxCount)
        {
            if (items.Count <= maxCount)
            {
                return items;
            }

            var step = (int)Math.Ceiling(items.Count / (double)maxCount);
            var sampled = new List<T>(maxCount);

            for (var i = 0; i < items.Count && sampled.Count < maxCount; i += step)
            {
                sampled.Add(items[i]);
            }

            return sampled;
        }

        private static Task RunOnStaThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "PdfExportStaWorker"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return tcs.Task;
        }

        private static void AddTableHeader(Table table, params string[] headers)
        {
            foreach (var header in headers)
            {
                table.AddHeaderCell(new Cell().Add(new Paragraph(header).SetBold().SetFontSize(9)).SetBackgroundColor(new DeviceRgb(235, 243, 250)));
            }
        }

        private static void AddTableCells(Table table, params string[] values)
        {
            foreach (var value in values)
            {
                table.AddCell(new Cell().Add(new Paragraph(value).SetFontSize(9)));
            }
        }

        private static string SanitizeFileNamePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "用户";
            }

            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "用户" : sanitized;
        }

        private (DateTime Start, DateTime End) GetSearchRange()
        {
            var start = StartDate.Date;
            var end = EndDate.Date.AddDays(1).AddTicks(-1);

            if (end < start)
            {
                end = start.AddDays(1).AddTicks(-1);
            }

            return (start, end);
        }

        private sealed class DetailExportData
        {
            public string Summary { get; set; } = string.Empty;
            public IReadOnlyList<CommandLogItem> CommandLogs { get; set; } = Array.Empty<CommandLogItem>();
            public IReadOnlyList<PressureLogItem> PressureLogs { get; set; } = Array.Empty<PressureLogItem>();
            public IReadOnlyList<EdemaLogItem> EdemaLogs { get; set; } = Array.Empty<EdemaLogItem>();
        }
    }

    public class TreatmentRecord : ObservableObject
    {
        private Guid _sessionId;
        private DateTime _startTime;
        private string _userName = string.Empty;
        private string _mode = string.Empty;
        private string _duration = string.Empty;
        private string _intensity = string.Empty;
        private string _completionRate = string.Empty;
        private string _status = string.Empty;

        public Guid SessionId { get => _sessionId; set => SetField(ref _sessionId, value); }
        public DateTime StartTime { get => _startTime; set => SetField(ref _startTime, value); }
        public string UserName { get => _userName; set => SetField(ref _userName, value); }
        public string Mode { get => _mode; set => SetField(ref _mode, value); }
        public string Duration { get => _duration; set => SetField(ref _duration, value); }
        public string Intensity { get => _intensity; set => SetField(ref _intensity, value); }
        public string CompletionRate { get => _completionRate; set => SetField(ref _completionRate, value); }
        public string Status { get => _status; set => SetField(ref _status, value); }
        public string StartTimeDisplay => StartTime.ToString("yyyy-MM-dd HH:mm");
    }

    public class HistoryCurvePoint
    {
        public HistoryCurvePoint(DateTime timestamp, double value)
        {
            Timestamp = timestamp;
            Value = value;
        }

        public DateTime Timestamp { get; }
        public double Value { get; }
        public string TimeDisplay => Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public class PressureLogItem
    {
        public PressureLogItem(DateTime timestamp, int sensorId, double currentValue, double maxValue, double threshold, bool isAlert, double latencyMs)
        {
            Timestamp = timestamp;
            SensorId = sensorId;
            CurrentValue = currentValue;
            MaxValue = maxValue;
            Threshold = threshold;
            IsAlert = isAlert;
            LatencyMs = latencyMs;
        }

        public DateTime Timestamp { get; }
        public int SensorId { get; }
        public double CurrentValue { get; }
        public double MaxValue { get; }
        public double Threshold { get; }
        public bool IsAlert { get; }
        public double LatencyMs { get; }
        public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
        public string ValueDisplay => $"{CurrentValue:F1} g";
        public string MaxDisplay => $"{MaxValue:F1} g";
        public string ThresholdDisplay => $"{Threshold:F1} g";
        public string LatencyDisplay => $"{LatencyMs:F0} ms";
    }

    public class EdemaLogItem
    {
        public EdemaLogItem(DateTime timestamp, int sensorId, double impedance, double edemaPercentage, double threshold, bool isAlert, double latencyMs)
        {
            Timestamp = timestamp;
            SensorId = sensorId;
            Impedance = impedance;
            EdemaPercentage = edemaPercentage;
            Threshold = threshold;
            IsAlert = isAlert;
            LatencyMs = latencyMs;
        }

        public DateTime Timestamp { get; }
        public int SensorId { get; }
        public double Impedance { get; }
        public double EdemaPercentage { get; }
        public double Threshold { get; }
        public bool IsAlert { get; }
        public double LatencyMs { get; }
        public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
        public string ImpedanceDisplay => $"{Impedance:F0} Ω";
        public string PercentageDisplay => $"{EdemaPercentage:F1}%";
        public string ThresholdDisplay => $"{Threshold:F1}%";
        public string LatencyDisplay => $"{LatencyMs:F0} ms";
    }
}
