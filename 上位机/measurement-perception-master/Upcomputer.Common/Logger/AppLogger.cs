using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Upcomputer.Common.Logger
{
    /// <summary>
    /// 应用程序日志工厂
    /// <para>在应用启动时调用 <see cref="Initialize"/> 初始化日志系统，随后通过 <see cref="CreateLogger{T}"/> 为各模块创建日志记录器。</para>
    /// </summary>
    public static class AppLogger
    {
        /// <summary>全局日志工厂实例，调用 Initialize 后赋值</summary>
        private static ILoggerFactory? _loggerFactory;

        /// <summary>
        /// 初始化日志系统
        /// <para>配置 Console 和 Debug 日志输出提供程序。必须在应用启动时先于此调用其他日志方法。</para>
        /// </summary>
        /// <param name="logDirectory">日志文件输出目录（预留，当前版本仅输出到控制台和调试窗口）</param>
        public static void Initialize(string logDirectory)
        {
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
                /*
                builder.AddFile(Path.Combine(logDirectory, "upcomputer-.log"),
                    options => options.FormatLogEntry = (entry) =>
                        $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.LogLevel}] {entry.Message}");
                */
            });
        }

        /// <summary>
        /// 为指定类型创建日志记录器
        /// </summary>
        /// <typeparam name="T">使用日志记录器的类型（通常传入当前类）</typeparam>
        /// <returns>类型化的 <see cref="ILogger{T}"/> 实例</returns>
        /// <exception cref="InvalidOperationException">日志系统尚未初始化时抛出</exception>
        public static ILogger<T> CreateLogger<T>() => _loggerFactory?.CreateLogger<T>()
            ?? throw new InvalidOperationException("Logger not initialized");
    }
}