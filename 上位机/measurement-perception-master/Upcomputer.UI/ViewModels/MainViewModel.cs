using System;
using System.Windows.Input;
using Upcomputer.Common.Enums;
using Upcomputer.Common.Models;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;
using Upcomputer.Core.Services;

namespace Upcomputer.UI.ViewModels
{
    /// <summary>
    /// Provides the top-level shell state and navigation coordination for the main UI.
    /// </summary>
    public class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ICommunicationService _communicationService;
        private readonly TreatmentEngine _treatmentEngine;
        private readonly IUserSelected _userSelected;
        private DashboardViewModel? _dashboardViewModel;
        private ControlPanelViewModel? _controlPanelViewModel;
        private HistoryViewModel? _historyViewModel;
        private SettingsViewModel? _settingsViewModel;

        private UserProfile? _currentUser;

        private object? _currentView;
        private SystemStatus _systemStatus = new();
        private string _currentTime = DateTime.Now.ToString("HH:mm:ss");


        /// <summary>
        /// Initializes a new instance of the <see cref="MainViewModel"/> class.
        /// </summary>
        /// <param name="communicationService">The device communication service.</param>
        /// <param name="treatmentEngine">The treatment engine.</param>
        /// <param name="userSelected">The user selection event source.</param>
        public MainViewModel(ICommunicationService communicationService, TreatmentEngine treatmentEngine, IUserSelected userSelected)
        {
            _communicationService = communicationService;
            _treatmentEngine = treatmentEngine;
            _userSelected = userSelected;

            // 订阅通信状态变化事件
            _communicationService.SystemStatusChanged += OnSystemStatusChanged;

            // 订阅用户选择事件（来自用户选择页），用于分发给其它 VM/服务
            _userSelected.UserSelected += OnUserSelected;

            NavigateCommand = new RelayCommand<string>(OnNavigate);
            UpdateTimeCommand = new RelayCommand(UpdateTime);

            // 定时更新系统时间
            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += (s, e) => UpdateTime();
            timer.Start();
        }

        /// <summary>
        /// Gets the currently selected user profile.
        /// </summary>
        public UserProfile? CurrentUser
        {
            get => _currentUser;
            private set => SetField(ref _currentUser, value);
        }

        /// <summary>
        /// Handles communication status updates from the device layer.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="status">The new system status snapshot.</param>
        private void OnSystemStatusChanged(object? sender, SystemStatus status)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;

            if (dispatcher is null || dispatcher.CheckAccess())
            {
                SystemStatus = status;
                System.Diagnostics.Debug.WriteLine($"状态更新: WiFi={status.WifiState}, 连接={status.IsDeviceConnected}, TCP={status.TcpLatency}ms, UDP={status.UdpLatency}ms");
                return;
            }

            dispatcher.Invoke(() =>
            {
                SystemStatus = status;
                System.Diagnostics.Debug.WriteLine($"状态更新: WiFi={status.WifiState}, 连接={status.IsDeviceConnected}, TCP={status.TcpLatency}ms, UDP={status.UdpLatency}ms");
            });
        }

        /// <summary>
        /// Releases event subscriptions owned by this view model.
        /// </summary>
        public void Dispose()
        {
            _communicationService.SystemStatusChanged -= OnSystemStatusChanged;
            _userSelected.UserSelected -= OnUserSelected;
        }

        /// <summary>
        /// Handles the user selection event from the user picker workflow.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The user selection event args.</param>
        private void OnUserSelected(object? sender, UserSelectedEventArgs e)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;

            if (dispatcher is null || dispatcher.CheckAccess())
            {
                CurrentUser = e.User;
                return;
            }

            dispatcher.Invoke(() =>
            {
                CurrentUser = e.User;
            });
        }

        /// <summary>
        /// Gets or sets the active view object displayed by the shell.
        /// </summary>
        public object? CurrentView
        {
            get => _currentView;
            set => SetField(ref _currentView, value);
        }

        /// <summary>
        /// Gets or sets the current system status snapshot.
        /// </summary>
        public SystemStatus SystemStatus
        {
            get => _systemStatus;
            set => SetField(ref _systemStatus, value);
        }

        /// <summary>
        /// Gets or sets the formatted current time string.
        /// </summary>
        public string CurrentTime
        {
            get => _currentTime;
            set => SetField(ref _currentTime, value);
        }

        /// <summary>
        /// Gets or sets the dashboard child view model.
        /// </summary>
        public DashboardViewModel? DashboardViewModel
        {
            get => _dashboardViewModel;
            set => SetField(ref _dashboardViewModel, value);
        }

        /// <summary>
        /// Gets or sets the control panel child view model.
        /// </summary>
        public ControlPanelViewModel? ControlPanelViewModel
        {
            get => _controlPanelViewModel;
            set => SetField(ref _controlPanelViewModel, value);
        }

        /// <summary>
        /// Gets or sets the history child view model.
        /// </summary>
        public HistoryViewModel? HistoryViewModel
        {
            get => _historyViewModel;
            set => SetField(ref _historyViewModel, value);
        }

        /// <summary>
        /// Gets or sets the settings child view model.
        /// </summary>
        public SettingsViewModel? SettingsViewModel
        {
            get => _settingsViewModel;
            set => SetField(ref _settingsViewModel, value);
        }

        /// <summary>
        /// Gets the navigation command.
        /// </summary>
        public ICommand NavigateCommand { get; }

        /// <summary>
        /// Gets the time refresh command.
        /// </summary>
        public ICommand UpdateTimeCommand { get; }

        /// <summary>
        /// Switches the active child view by name.
        /// </summary>
        /// <param name="viewName">The target view identifier.</param>
        private void OnNavigate(string viewName)
        {
            CurrentView = viewName switch
            {
                "Dashboard" => DashboardViewModel,
                "Control" => ControlPanelViewModel,
                "History" => HistoryViewModel,
                "Settings" => SettingsViewModel,
                _ => CurrentView
            };
        }

        /// <summary>
        /// Updates the displayed time string.
        /// </summary>
        private void UpdateTime()
        {
            CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }


    }
}
