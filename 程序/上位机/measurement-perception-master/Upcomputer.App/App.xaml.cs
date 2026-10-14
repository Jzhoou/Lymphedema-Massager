using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Upcomputer.Communication;
using Upcomputer.Common.Logger;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Services;
using Upcomputer.Data.Database;
using Upcomputer.Data.Repositories;
using Upcomputer.UI.ViewModels;
using Upcomputer.UI.Views;

namespace Upcomputer.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IHost? AppHost { get; private set; }
        public IServiceProvider? ServiceProvider { get; set; }

        public App()
        {
            AppHost = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
                    logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Connection", LogLevel.Warning);
                    logging.AddFilter("Microsoft.EntityFrameworkCore.Infrastructure", LogLevel.Warning);
                })
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(services);
                })
                .Build();

            ServiceProvider = AppHost.Services;
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // 初始化日志
            var logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Upcomputer", "Logs");
            System.IO.Directory.CreateDirectory(logDir);
            AppLogger.Initialize(logDir);

            // 注册通信服务：可切换 WiFi / 串口
            services.AddSingleton<WifiCommunication>();
            services.AddSingleton<SerialCommunication>();
            services.AddSingleton<ICommunicationService, CommunicationServiceRouter>();

            // 注册业务服务
            services.AddSingleton<TreatmentEngine>();

            // 事件总线用于解耦 UI 与业务层
            services.AddSingleton<IUserSelected, UserSelectedBus>();

            // 注册数据库上下文与仓储
            DatabaseSettings.EnsureDatabaseDirectory();
            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlite($"Data Source={DatabaseSettings.DatabaseFilePath}"));
            services.AddSingleton<DatabaseInitializer>();
            services.AddSingleton<ITreatmentRepository, TreatmentRepository>();
            services.AddSingleton<IPressureLogRepository, PressureLogRepository>();

            // ViewModel 统一按单例注册
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<UserInterfaceViewModel>();
            services.AddSingleton<UserManagementViewModel>();
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<ControlPanelViewModel>();
            services.AddSingleton<HistoryViewModel>();
            services.AddSingleton<SettingsViewModel>();

            // 注册主窗口
            services.AddSingleton<MainWindow>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JHaF5cWWRCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdlWXteeXRRQ2JcVUd2W0pWYEo=");

            try
            {
                await ServiceProvider!.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"数据库初始化失败：{ex}");
            }

            await AppHost!.StartAsync();


            var mainWindow = ServiceProvider!.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                var treatmentEngine = ServiceProvider?.GetRequiredService<TreatmentEngine>();
                var treatmentRepository = ServiceProvider?.GetRequiredService<ITreatmentRepository>();

                if (treatmentEngine?.CurrentSession is { IsRunning: true } session && session.SessionId != Guid.Empty)
                {
                    treatmentEngine.StopTreatment();
                    await treatmentRepository!.EndTreatmentSessionAsync(session.SessionId, session.EndTime ?? DateTime.Now);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"退出时结束治疗会话失败：{ex}");
            }

            await AppHost!.StopAsync();
            AppHost.Dispose();

            base.OnExit(e);
        }
    }

}
