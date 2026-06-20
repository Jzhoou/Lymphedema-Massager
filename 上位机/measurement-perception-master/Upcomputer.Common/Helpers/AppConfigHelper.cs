using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using Upcomputer.Common.Constants;

namespace Upcomputer.Common.Helpers
{
    /// <summary>
    /// 应用程序配置数据模型
    /// <para>持久化保存用户最后使用的通信连接参数、告警阈值等配置项。</para>
    /// </summary>
    public class AppConfig
    {
        /// <summary>最近一次使用的 WiFi 连接 IP 地址</summary>
        public string LastWifiIp { get; set; } = AppConstants.DefaultWifiIp;

        /// <summary>最近一次使用的 WiFi 连接端口号</summary>
        public int LastWifiPort { get; set; } = AppConstants.DefaultWifiPort;

        /// <summary>最近一次使用的串口名称（如 COM3）</summary>
        public string LastSerialPort { get; set; } = "COM3";

        /// <summary>最近一次使用的串口波特率</summary>
        public int LastBaudRate { get; set; } = 115200;

        /// <summary>用户设定的压力告警阈值（单位：g）</summary>
        public double PressureThreshold { get; set; } = AppConstants.DefaultPressureMaxThreshold;

        /// <summary>用户设定的水肿百分比告警阈值（单位：%）</summary>
        public double EdemaThreshold { get; set; } = AppConstants.DefaultEdemaMaxThreshold;

        /// <summary>3D 圆环插值单帧最大位移（mm）</summary>
        public double Arm3DMaxStepMm { get; set; } = 12.0;

        /// <summary>3D 圆环位置纠偏系数（0-1）</summary>
        public double Arm3DCorrectionGain { get; set; } = 0.28;

        /// <summary>3D 圆环与真实位置偏差超过该值时直接同步（mm）</summary>
        public double Arm3DHardSyncThresholdMm { get; set; } = 20.0;
    }

    /// <summary>
    /// 应用程序配置文件读写助手
    /// <para>将 <see cref="AppConfig"/> 以 JSON 格式存储到本地 <c>%LocalAppData%/Upcomputer/Config/</c> 目录，
    /// 首次运行时自动创建默认配置文件。</para>
    /// </summary>
    public static class AppConfigHelper
    {
        /// <summary>配置文件存储路径：%LocalAppData%/Upcomputer/Config/appconfig.json</summary>
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Upcomputer", "Config", "appconfig.json");

        /// <summary>内存缓存的配置实例，避免重复读取磁盘</summary>
        private static AppConfig? _config;

        /// <summary>
        /// 加载应用程序配置
        /// <para>优先返回内存缓存；若文件不存在则创建默认配置并持久化；读取异常时返回默认实例。</para>
        /// </summary>
        /// <returns>加载或新建的 <see cref="AppConfig"/> 实例</returns>
        public static AppConfig Load()
        {
            if (_config != null) return _config;

            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    _config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                    // 回写一次，确保新增配置项自动补齐到本地文件
                    Save(_config);
                }
                else
                {
                    _config = new AppConfig();
                    Save(_config);
                }
            }
            catch
            {
                _config = new AppConfig();
            }

            return _config;
        }

        /// <summary>
        /// 保存应用程序配置到磁盘
        /// </summary>
        /// <param name="config">待保存的配置实例</param>
        public static void Save(AppConfig config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                _config = config;
            }
            catch { }
        }
    }
}