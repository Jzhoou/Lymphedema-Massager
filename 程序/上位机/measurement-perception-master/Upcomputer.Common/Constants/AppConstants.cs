using System;
using System.Collections.Generic;
using System.Text;

namespace Upcomputer.Common.Constants
{
    /// <summary>
    /// 应用程序全局常量定义
    /// <para>包含应用名称、版本、通信配置、UI刷新参数及告警阈值默认值等核心常量。</para>
    /// </summary>
    public static class AppConstants
    {
        /// <summary>应用程序名称</summary>
        public const string AppName = "康复治疗上位机系统";

        /// <summary>当前应用版本号</summary>
        public const string Version = "1.0.0";

        /// <summary>UI 界面数据刷新间隔（毫秒），控制仪表盘、曲线等控件的刷新频率</summary>
        public const int UIRefreshIntervalMs = 100;

        // ---- 通信配置 ----

        /// <summary>默认 WiFi TCP 通信端口号</summary>
        public const int DefaultWifiPort = 8080;

        /// <summary>默认 WiFi 连接 IP 地址（设备 AP 模式下的固定地址）</summary>
        public const string DefaultWifiIp = "192.168.4.1";

        /// <summary>默认串口波特率（与下位机固件约定的 115200 baud）</summary>
        public const int DefaultBaudRate = 460800;

        // ---- 告警阈值默认值 ----

        /// <summary>默认压力告警阈值（单位：g），超过此值触发告警提示</summary>
        public const double DefaultPressureMaxThreshold = 80.0;

        /// <summary>默认水肿百分比告警阈值（单位：%），超过此值触发告警提示</summary>
        public const double DefaultEdemaMaxThreshold = 30.0;
    }

}