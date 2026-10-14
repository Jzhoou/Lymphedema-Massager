using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Upcomputer.Common.Enums;
using Upcomputer.Core.Models;

namespace Upcomputer.Data.Database
{
    /// <summary>
    /// 数据库初始化器
    /// <para>
    /// 在应用启动时执行数据库初始化流程：
    /// 1. 尝试 EF Core 迁移（失败时回退到 EnsureCreated）
    /// 2. 使用原生 SQL 补齐表结构（IF NOT EXISTS），兼容旧版数据库升级
    /// 3. 种子数据：创建默认 admin 用户、医生建议、治疗提醒
    /// </para>
    /// </summary>
    public sealed class DatabaseInitializer
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ILogger<DatabaseInitializer> _logger;

        public DatabaseInitializer(IDbContextFactory<AppDbContext> contextFactory, ILogger<DatabaseInitializer> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        /// <summary>
        /// 执行数据库初始化（迁移/建表/种子数据）
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            DatabaseSettings.EnsureDatabaseDirectory();

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

                try
                {
                    await context.Database.MigrateAsync(cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "数据库迁移不可用，改为使用 EnsureCreated 初始化。");
                    await context.Database.EnsureCreatedAsync(cancellationToken);
                }
                catch (SqliteException ex)
                {
                    _logger.LogWarning(ex, "数据库迁移失败，改为使用 EnsureCreated 初始化。");
                    await context.Database.EnsureCreatedAsync(cancellationToken);
                }

                await EnsureSchemaAsync(context, cancellationToken);

                await SeedAsync(context, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
            {
                _logger.LogError(ex, "数据库初始化失败，应用将使用降级内存模式。");
            }
        }

        private static async Task EnsureSchemaAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            // 说明：当前项目未使用 EF migrations，首次运行或升级后旧库可能缺少新表。
            // 这里使用 SQLite 的 IF NOT EXISTS 语句补齐表结构，避免“no such table”导致启动失败。
            var sql = new StringBuilder();
            sql.AppendLine("PRAGMA foreign_keys = ON;");
            sql.AppendLine("CREATE TABLE IF NOT EXISTS UserProfiles (");
            sql.AppendLine("    UserId TEXT NOT NULL PRIMARY KEY,");
            sql.AppendLine("    UserName TEXT NOT NULL,");
            sql.AppendLine("    Gender TEXT NULL,");
            sql.AppendLine("    BirthDate TEXT NULL,");
            sql.AppendLine("    PhoneNumber TEXT NULL,");
            sql.AppendLine("    HeightCm REAL NULL,");
            sql.AppendLine("    WeightKg REAL NULL,");
            sql.AppendLine("    CreatedAt TEXT NOT NULL");
            sql.AppendLine(");");

            sql.AppendLine("CREATE TABLE IF NOT EXISTS TreatmentSessions (");
            sql.AppendLine("    SessionId TEXT NOT NULL PRIMARY KEY,");
            sql.AppendLine("    UserId TEXT NOT NULL DEFAULT 'admin',");
            sql.AppendLine("    Mode INTEGER NOT NULL,");
            sql.AppendLine("    TotalDuration INTEGER NOT NULL,");
            sql.AppendLine("    ElapsedTime INTEGER NOT NULL,");
            sql.AppendLine("    Intensity REAL NOT NULL,");
            sql.AppendLine("    IsRunning INTEGER NOT NULL,");
            sql.AppendLine("    Progress REAL NOT NULL,");
            sql.AppendLine("    StartTime TEXT NOT NULL,");
            sql.AppendLine("    EndTime TEXT NULL,");
            sql.AppendLine("    DoctorAdvice TEXT NULL,");
            sql.AppendLine("    FOREIGN KEY(UserId) REFERENCES UserProfiles(UserId) ON DELETE RESTRICT");
            sql.AppendLine(");");

            sql.AppendLine("CREATE TABLE IF NOT EXISTS PressureLogs (");
            sql.AppendLine("    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,");
            sql.AppendLine("    SessionId TEXT NOT NULL,");
            sql.AppendLine("    SensorId INTEGER NOT NULL,");
            sql.AppendLine("    CurrentValue REAL NOT NULL,");
            sql.AppendLine("    MaxValue REAL NOT NULL,");
            sql.AppendLine("    Threshold REAL NOT NULL,");
            sql.AppendLine("    IsAlert INTEGER NOT NULL,");
            sql.AppendLine("    LatencyMs REAL NOT NULL DEFAULT 0,");
            sql.AppendLine("    Timestamp TEXT NOT NULL,");
            sql.AppendLine("    FOREIGN KEY(SessionId) REFERENCES TreatmentSessions(SessionId) ON DELETE CASCADE");
            sql.AppendLine(");");

            sql.AppendLine("CREATE TABLE IF NOT EXISTS CommandLogs (");
            sql.AppendLine("    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,");
            sql.AppendLine("    SessionId TEXT NOT NULL,");
            sql.AppendLine("    Timestamp TEXT NOT NULL,");
            sql.AppendLine("    CommandName TEXT NOT NULL,");
            sql.AppendLine("    CommandDetails TEXT NOT NULL,");
            sql.AppendLine("    FOREIGN KEY(SessionId) REFERENCES TreatmentSessions(SessionId) ON DELETE CASCADE");
            sql.AppendLine(");");

            sql.AppendLine("CREATE TABLE IF NOT EXISTS EdemaLogs (");
            sql.AppendLine("    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,");
            sql.AppendLine("    SessionId TEXT NOT NULL,");
            sql.AppendLine("    SensorId INTEGER NOT NULL,");
            sql.AppendLine("    Impedance REAL NOT NULL,");
            sql.AppendLine("    EdemaPercentage REAL NOT NULL,");
            sql.AppendLine("    Threshold REAL NOT NULL,");
            sql.AppendLine("    IsAlert INTEGER NOT NULL,");
            sql.AppendLine("    LatencyMs REAL NOT NULL DEFAULT 0,");
            sql.AppendLine("    Timestamp TEXT NOT NULL,");
            sql.AppendLine("    FOREIGN KEY(SessionId) REFERENCES TreatmentSessions(SessionId) ON DELETE CASCADE");
            sql.AppendLine(");");

            sql.AppendLine("CREATE TABLE IF NOT EXISTS DoctorAdviceRecords (");
            sql.AppendLine("    Id TEXT NOT NULL PRIMARY KEY,");
            sql.AppendLine("    Content TEXT NOT NULL,");
            sql.AppendLine("    UpdatedAt TEXT NOT NULL,");
            sql.AppendLine("    IsActive INTEGER NOT NULL");
            sql.AppendLine(");");

            sql.AppendLine("CREATE TABLE IF NOT EXISTS TreatmentReminders (");
            sql.AppendLine("    ReminderId TEXT NOT NULL PRIMARY KEY,");
            sql.AppendLine("    Content TEXT NOT NULL,");
            sql.AppendLine("    DueAt TEXT NOT NULL,");
            sql.AppendLine("    IsCompleted INTEGER NOT NULL,");
            sql.AppendLine("    CreatedAt TEXT NOT NULL");
            sql.AppendLine(");");

            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_TreatmentSessions_StartTime ON TreatmentSessions(StartTime);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_TreatmentSessions_IsRunning_StartTime ON TreatmentSessions(IsRunning, StartTime);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_UserProfiles_UserName ON UserProfiles(UserName);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_PressureLogs_SessionId_Timestamp ON PressureLogs(SessionId, Timestamp);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_PressureLogs_Timestamp ON PressureLogs(Timestamp);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_EdemaLogs_SessionId_Timestamp ON EdemaLogs(SessionId, Timestamp);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_EdemaLogs_Timestamp ON EdemaLogs(Timestamp);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_CommandLogs_SessionId_Timestamp ON CommandLogs(SessionId, Timestamp);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_CommandLogs_Timestamp ON CommandLogs(Timestamp);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_DoctorAdviceRecords_IsActive ON DoctorAdviceRecords(IsActive);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_DoctorAdviceRecords_UpdatedAt ON DoctorAdviceRecords(UpdatedAt);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_TreatmentReminders_IsCompleted_DueAt ON TreatmentReminders(IsCompleted, DueAt);");
            sql.AppendLine("CREATE INDEX IF NOT EXISTS IX_TreatmentReminders_DueAt ON TreatmentReminders(DueAt);");

            await context.Database.ExecuteSqlRawAsync(sql.ToString(), cancellationToken);
            await TryAddColumnAsync(context, "UserProfiles", "Gender TEXT NULL", cancellationToken);
            await TryAddColumnAsync(context, "TreatmentSessions", "UserId TEXT NULL", cancellationToken);
            await TryCreateIndexAsync(context, "IX_TreatmentSessions_UserId", "TreatmentSessions", "UserId", cancellationToken);
            await TryAddColumnAsync(context, "PressureLogs", "LatencyMs REAL NOT NULL DEFAULT 0", cancellationToken);
            await TryAddColumnAsync(context, "EdemaLogs", "LatencyMs REAL NOT NULL DEFAULT 0", cancellationToken);
        }

        private static async Task TryAddColumnAsync(AppDbContext context, string tableName, string columnDefinition, CancellationToken cancellationToken)
        {
            var columnName = columnDefinition.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return;
            }

            if (await HasColumnAsync(context, tableName, columnName, cancellationToken))
            {
                return;
            }

            try
            {
                var sql = $"ALTER TABLE {tableName} ADD COLUMN {columnDefinition};";
                await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
            }
        }

        private static async Task<bool> HasColumnAsync(AppDbContext context, string tableName, string columnName, CancellationToken cancellationToken)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";

            if (command.Connection?.State != System.Data.ConnectionState.Open)
            {
                await command.Connection!.OpenAsync(cancellationToken);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task TryCreateIndexAsync(AppDbContext context, string indexName, string tableName, string columnName, CancellationToken cancellationToken)
        {
            try
            {
                var sql = $"CREATE INDEX IF NOT EXISTS {indexName} ON {tableName}({columnName});";
                await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }
            catch (SqliteException ex) when (ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase))
            {
            }
        }

        private static async Task SeedAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            await EnsureAdminUserAsync(context, cancellationToken);
            await BackfillLegacySessionsAsync(context, cancellationToken);

            if (!await context.DoctorAdviceRecords.AnyAsync(cancellationToken))
            {
                context.DoctorAdviceRecords.Add(new DoctorAdviceRecord
                {
                    Id = Guid.NewGuid(),
                    Content = "建议每日进行2-3次康复训练，每次20-30分钟。注意观察水肿变化，如持续升高请及时就医。按摩强度建议控制在50%-70%之间。",
                    UpdatedAt = DateTime.Now,
                    IsActive = true
                });
            }

            if (!await context.TreatmentReminders.AnyAsync(cancellationToken))
            {
                context.TreatmentReminders.AddRange(
                    new TreatmentReminder
                    {
                        ReminderId = Guid.NewGuid(),
                        Content = "今日 15:30 进行下一次康复训练",
                        DueAt = DateTime.Now.AddHours(4),
                        CreatedAt = DateTime.Now,
                        IsCompleted = false
                    },
                    new TreatmentReminder
                    {
                        ReminderId = Guid.NewGuid(),
                        Content = "本周完成 2 次康复训练",
                        DueAt = DateTime.Now.AddDays(2),
                        CreatedAt = DateTime.Now,
                        IsCompleted = false
                    });
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        private static async Task EnsureAdminUserAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            var adminExists = await context.UserProfiles.AnyAsync(x => x.UserId == "admin", cancellationToken);
            if (adminExists)
            {
                return;
            }

            context.UserProfiles.Add(new UserProfile
            {
                UserId = "admin",
                UserName = "admin",
                CreatedAt = DateTime.Now
            });

            await context.SaveChangesAsync(cancellationToken);
        }

        private static async Task BackfillLegacySessionsAsync(AppDbContext context, CancellationToken cancellationToken)
        {
            await EnsureAdminUserAsync(context, cancellationToken);

            await context.Database.ExecuteSqlRawAsync(
                "UPDATE TreatmentSessions SET UserId = 'admin' WHERE UserId IS NULL OR UserId = '' OR NOT EXISTS (SELECT 1 FROM UserProfiles WHERE UserProfiles.UserId = TreatmentSessions.UserId);",
                cancellationToken);
        }
    }
}
