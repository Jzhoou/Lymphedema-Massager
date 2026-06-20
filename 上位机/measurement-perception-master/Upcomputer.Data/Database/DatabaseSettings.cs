using System;
using System.IO;

namespace Upcomputer.Data.Database
{
    /// <summary>
    /// 数据库路径配置
    /// <para>定义 SQLite 数据库文件的存储位置，位于 <c>%LocalAppData%/Upcomputer/</c> 目录下。</para>
    /// </summary>
    public static class DatabaseSettings
    {
        /// <summary>数据库文件所在目录路径（%LocalAppData%/Upcomputer/）</summary>
        public static string DatabaseDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Upcomputer");

        /// <summary>SQLite 数据库文件完整路径</summary>
        public static string DatabaseFilePath => Path.Combine(DatabaseDirectory, "Upcomputer.db");

        /// <summary>确保数据库目录存在（不存在则创建）</summary>
        public static void EnsureDatabaseDirectory()
        {
            Directory.CreateDirectory(DatabaseDirectory);
        }
    }
}