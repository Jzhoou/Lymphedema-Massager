using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Upcomputer.Common.Enums;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;
using Upcomputer.Data.Database;

namespace Upcomputer.Data.Repositories
{
    /// <summary>
    /// 治疗会话仓储实现
    /// <para>
    /// 实现 <see cref="ITreatmentRepository"/> 接口，提供治疗会话及其关联数据的完整 CRUD 操作。
    /// 采用"数据库优先 + 降级内存"双模式：正常情况下写入 SQLite，数据库异常时自动切换为纯内存模式，
    /// 确保应用在数据库不可用时仍能正常运行。
    /// </para>
    /// </summary>
    public class TreatmentRepository : ITreatmentRepository
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ILogger<TreatmentRepository> _logger;
        private readonly List<TreatmentSession> _fallbackSessions = new();
        private readonly List<PressureData> _fallbackPressureLogs = new();
        private readonly List<EdemaData> _fallbackEdemaLogs = new();
        private readonly List<CommandLog> _fallbackCommandLogs = new();
        private readonly List<TreatmentReminder> _fallbackReminders = new()
        {
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
            }
        };
        private DoctorAdviceRecord? _fallbackAdvice = new()
        {
            Id = Guid.NewGuid(),
            Content = "建议每日进行2-3次康复训练，每次20-30分钟。注意观察水肿变化，如持续升高请及时就医。按摩强度建议控制在50%-70%之间。",
            UpdatedAt = DateTime.Now,
            IsActive = true
        };
        private bool _useFallback;
        private readonly object _gate = new();
        private const int TransientRetryCount = 3;
        private const int TransientRetryDelayMs = 120;

        public TreatmentRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<TreatmentRepository> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        private static SqliteException? ExtractSqliteException(Exception ex)
        {
            if (ex is SqliteException sqliteEx)
            {
                return sqliteEx;
            }

            if (ex is DbUpdateException dbUpdateEx && dbUpdateEx.InnerException is SqliteException innerSqlite)
            {
                return innerSqlite;
            }

            return null;
        }

        private static bool IsTransientSqliteException(Exception ex)
        {
            var sqliteEx = ExtractSqliteException(ex);
            if (sqliteEx == null)
            {
                return false;
            }

            return sqliteEx.SqliteErrorCode == 5
                || sqliteEx.SqliteErrorCode == 6
                || sqliteEx.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
                || sqliteEx.Message.Contains("database is busy", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<Guid> StartTreatmentSessionAsync(TreatmentSession session)
        {
            session.SessionId = session.SessionId == Guid.Empty ? Guid.NewGuid() : session.SessionId;
            session.StartTime = session.StartTime == default ? DateTime.Now : session.StartTime;
            session.IsRunning = true;
            session.UserId = string.IsNullOrWhiteSpace(session.UserId) ? "admin" : session.UserId;

            if (_useFallback)
            {
                lock (_gate)
                {
                    _fallbackSessions.RemoveAll(x => x.SessionId == session.SessionId);
                    _fallbackSessions.Add(session);
                }

                return session.SessionId;
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var context = await _contextFactory.CreateDbContextAsync();
                    var userExists = await context.UserProfiles.AnyAsync(x => x.UserId == session.UserId);
                    if (!userExists)
                    {
                        session.UserId = "admin";
                    }

                    // Session.User 可能来自其他 DbContext（UI 选择用户对象），
                    // 直接 Add(session) 会把该导航对象误判为新增用户并触发 UserProfiles 唯一键冲突。
                    // 这里仅使用 UserId 外键持久化会话，避免重复插入用户档案。
                    session.User = null;

                    context.TreatmentSessions.Add(session);
                    await context.SaveChangesAsync();

                    lock (_gate)
                    {
                        _fallbackSessions.RemoveAll(x => x.SessionId == session.SessionId);
                        _fallbackSessions.Add(session);
                    }

                    return session.SessionId;
                }
                catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
                {
                    if (IsTransientSqliteException(ex) && attempt < TransientRetryCount)
                    {
                        _logger.LogWarning(ex, "启动治疗会话写入遇到数据库忙，准备第 {Retry} 次重试。", attempt + 1);
                        await Task.Delay(TransientRetryDelayMs * (attempt + 1));
                        continue;
                    }

                    if (IsTransientSqliteException(ex))
                    {
                        _logger.LogError(ex, "启动治疗会话写入重试失败，保持数据库模式，不切换降级内存。");
                        throw;
                    }

                    _logger.LogDebug(ex, "启动治疗会话失败，切换为降级内存模式。");
                    _useFallback = true;
                    return await StartTreatmentSessionAsync(session);
                }
            }
        }

        public async Task EndTreatmentSessionAsync(Guid sessionId, DateTime endTime)
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    var session = _fallbackSessions.FirstOrDefault(x => x.SessionId == sessionId);
                    if (session != null)
                    {
                        session.IsRunning = false;
                        session.EndTime = endTime;
                    }
                }

                return;
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var session = await context.TreatmentSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId);
                if (session == null)
                {
                    return;
                }

                session.IsRunning = false;
                session.EndTime = endTime;
                await context.SaveChangesAsync();

                lock (_gate)
                {
                    var fallbackSession = _fallbackSessions.FirstOrDefault(x => x.SessionId == sessionId);
                    if (fallbackSession != null)
                    {
                        fallbackSession.IsRunning = false;
                        fallbackSession.EndTime = endTime;
                    }
                }
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "结束治疗会话失败，切换为降级内存模式。");
                _useFallback = true;
                await EndTreatmentSessionAsync(sessionId, endTime);
            }
        }

        public async Task<int> GetSessionCountAsync(DateTime startDate, DateTime endDate)
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackSessions.Count(x => x.StartTime >= startDate && x.StartTime <= endDate);
                }
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var context = await _contextFactory.CreateDbContextAsync();
                    return await context.TreatmentSessions.AsNoTracking().CountAsync(x => x.StartTime >= startDate && x.StartTime <= endDate);
                }
                catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
                {
                    if (IsTransientSqliteException(ex) && attempt < TransientRetryCount)
                    {
                        _logger.LogWarning(ex, "统计会话数量遇到数据库忙，准备第 {Retry} 次重试。", attempt + 1);
                        await Task.Delay(TransientRetryDelayMs * (attempt + 1));
                        continue;
                    }

                    if (IsTransientSqliteException(ex))
                    {
                        _logger.LogError(ex, "统计会话数量重试失败，保持数据库模式，不切换降级内存。");
                        throw;
                    }

                    _logger.LogDebug(ex, "统计会话数量失败，切换为降级内存模式。");
                    _useFallback = true;
                    return await GetSessionCountAsync(startDate, endDate);
                }
            }
        }

        public async Task<IReadOnlyList<TreatmentSession>> GetSessionsAsync(DateTime startDate, DateTime endDate, int pageIndex = 1, int pageSize = 20)
        {
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Max(1, pageSize);

            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackSessions
                        .Where(x => x.StartTime >= startDate && x.StartTime <= endDate)
                        .OrderByDescending(x => x.StartTime)
                        .Skip((pageIndex - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();
                }
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var context = await _contextFactory.CreateDbContextAsync();
                    return await context.TreatmentSessions
                        .AsNoTracking()
                        .Include(x => x.User)
                        .Where(x => x.StartTime >= startDate && x.StartTime <= endDate)
                        .OrderByDescending(x => x.StartTime)
                        .Skip((pageIndex - 1) * pageSize)
                        .Take(pageSize)
                        .ToListAsync();
                }
                catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
                {
                    if (IsTransientSqliteException(ex) && attempt < TransientRetryCount)
                    {
                        _logger.LogWarning(ex, "查询会话列表遇到数据库忙，准备第 {Retry} 次重试。", attempt + 1);
                        await Task.Delay(TransientRetryDelayMs * (attempt + 1));
                        continue;
                    }

                    if (IsTransientSqliteException(ex))
                    {
                        _logger.LogError(ex, "查询会话列表重试失败，保持数据库模式，不切换降级内存。");
                        throw;
                    }

                    _logger.LogDebug(ex, "查询会话列表失败，切换为降级内存模式。");
                    _useFallback = true;
                    return await GetSessionsAsync(startDate, endDate, pageIndex, pageSize);
                }
            }
        }

        public async Task<TreatmentSession?> GetSessionDetailsAsync(Guid sessionId)
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackSessions.FirstOrDefault(x => x.SessionId == sessionId);
                }
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var context = await _contextFactory.CreateDbContextAsync();
                    return await context.TreatmentSessions
                        .AsNoTracking()
                        .Include(x => x.User)
                        .Include(x => x.CommandLogs)
                        .FirstOrDefaultAsync(x => x.SessionId == sessionId);
                }
                catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
                {
                    if (IsTransientSqliteException(ex) && attempt < TransientRetryCount)
                    {
                        _logger.LogWarning(ex, "查询会话详情遇到数据库忙，准备第 {Retry} 次重试。", attempt + 1);
                        await Task.Delay(TransientRetryDelayMs * (attempt + 1));
                        continue;
                    }

                    if (IsTransientSqliteException(ex))
                    {
                        _logger.LogError(ex, "查询会话详情重试失败，保持数据库模式，不切换降级内存。");
                        throw;
                    }

                    _logger.LogDebug(ex, "查询会话详情失败，切换为降级内存模式。");
                    _useFallback = true;
                    return await GetSessionDetailsAsync(sessionId);
                }
            }
        }

        public async Task<int> GetPressureLogCountAsync(Guid sessionId)
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackPressureLogs.Count(x => x.SessionId == sessionId);
                }
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                return await context.PressureDataLogs.AsNoTracking().CountAsync(x => x.SessionId == sessionId);
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "统计压力日志数量失败，切换为降级内存模式。");
                _useFallback = true;
                return await GetPressureLogCountAsync(sessionId);
            }
        }

        public async Task<IReadOnlyList<PressureData>> GetPressureLogsAsync(Guid sessionId, int pageIndex = 1, int pageSize = 60)
        {
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Max(1, pageSize);

            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackPressureLogs
                        .Where(x => x.SessionId == sessionId)
                        .OrderBy(x => x.Timestamp)
                        .ThenBy(x => x.SensorId)
                        .Skip((pageIndex - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();
                }
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                return await context.PressureDataLogs
                    .AsNoTracking()
                    .Where(x => x.SessionId == sessionId)
                    .OrderBy(x => x.Timestamp)
                    .ThenBy(x => x.SensorId)
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "查询压力日志失败，切换为降级内存模式。");
                _useFallback = true;
                return await GetPressureLogsAsync(sessionId, pageIndex, pageSize);
            }
        }

        public async Task<int> GetEdemaLogCountAsync(Guid sessionId)
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackEdemaLogs.Count(x => x.SessionId == sessionId);
                }
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                return await context.EdemaDataLogs.AsNoTracking().CountAsync(x => x.SessionId == sessionId);
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "统计水肿日志数量失败，切换为降级内存模式。");
                _useFallback = true;
                return await GetEdemaLogCountAsync(sessionId);
            }
        }

        public async Task<int> GetCommandLogCountAsync(Guid sessionId)
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackCommandLogs.Count(x => x.SessionId == sessionId);
                }
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                return await context.CommandLogs.AsNoTracking().CountAsync(x => x.SessionId == sessionId);
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogError(ex, "统计控制指令数量失败，切换为降级内存模式。");
                _useFallback = true;
                return await GetCommandLogCountAsync(sessionId);
            }
        }

        public async Task<IReadOnlyList<CommandLog>> GetCommandLogsAsync(Guid sessionId, int pageIndex = 1, int pageSize = 60)
        {
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Max(1, pageSize);

            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackCommandLogs
                        .Where(x => x.SessionId == sessionId)
                        .OrderBy(x => x.Timestamp)
                        .Skip((pageIndex - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();
                }
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                return await context.CommandLogs
                    .AsNoTracking()
                    .Where(x => x.SessionId == sessionId)
                    .OrderBy(x => x.Timestamp)
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogError(ex, "查询控制指令失败，切换为降级内存模式。");
                _useFallback = true;
                return await GetCommandLogsAsync(sessionId, pageIndex, pageSize);
            }
        }

        public async Task<bool> DeleteTreatmentSessionAsync(Guid sessionId)
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    var session = _fallbackSessions.FirstOrDefault(x => x.SessionId == sessionId);
                    if (session == null)
                    {
                        return false;
                    }

                    _fallbackSessions.Remove(session);
                    _fallbackPressureLogs.RemoveAll(x => x.SessionId == sessionId);
                    _fallbackEdemaLogs.RemoveAll(x => x.SessionId == sessionId);
                    _fallbackCommandLogs.RemoveAll(x => x.SessionId == sessionId);
                    return true;
                }
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var session = await context.TreatmentSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId);
                if (session == null)
                {
                    return false;
                }

                context.TreatmentSessions.Remove(session);
                await context.SaveChangesAsync();

                lock (_gate)
                {
                    _fallbackSessions.RemoveAll(x => x.SessionId == sessionId);
                    _fallbackPressureLogs.RemoveAll(x => x.SessionId == sessionId);
                    _fallbackEdemaLogs.RemoveAll(x => x.SessionId == sessionId);
                    _fallbackCommandLogs.RemoveAll(x => x.SessionId == sessionId);
                }

                return true;
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogError(ex, "删除治疗会话失败，切换为降级内存模式。");
                _useFallback = true;
                return await DeleteTreatmentSessionAsync(sessionId);
            }
        }

        public async Task<IReadOnlyList<EdemaData>> GetEdemaLogsAsync(Guid sessionId, int pageIndex = 1, int pageSize = 60)
        {
            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Max(1, pageSize);

            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackEdemaLogs
                        .Where(x => x.SessionId == sessionId)
                        .OrderBy(x => x.Timestamp)
                        .Skip((pageIndex - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();
                }
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                return await context.EdemaDataLogs
                    .AsNoTracking()
                    .Where(x => x.SessionId == sessionId)
                    .OrderBy(x => x.Timestamp)
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogError(ex, "查询水肿日志失败，切换为降级内存模式。");
                _useFallback = true;
                return await GetEdemaLogsAsync(sessionId, pageIndex, pageSize);
            }
        }

        public async Task AddPressureLogsAsync(Guid sessionId, IReadOnlyCollection<PressureData> pressureLogs)
        {
            if (pressureLogs.Count == 0)
            {
                return;
            }

            foreach (var log in pressureLogs)
            {
                log.SessionId = sessionId;
            }

            if (_useFallback)
            {
                lock (_gate)
                {
                    _fallbackPressureLogs.AddRange(pressureLogs);
                    var session = _fallbackSessions.FirstOrDefault(x => x.SessionId == sessionId);
                    if (session != null)
                    {
                        foreach (var log in pressureLogs)
                        {
                            session.PressureLogs.Add(log);
                        }
                    }
                }

                return;
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var context = await _contextFactory.CreateDbContextAsync();
                    context.ChangeTracker.AutoDetectChangesEnabled = false;
                    context.PressureDataLogs.AddRange(pressureLogs);
                    await context.SaveChangesAsync();

                    lock (_gate)
                    {
                        var session = _fallbackSessions.FirstOrDefault(x => x.SessionId == sessionId);
                        if (session != null)
                        {
                            foreach (var log in pressureLogs)
                            {
                                session.PressureLogs.Add(log);
                            }
                        }
                    }

                    return;
                }
                catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
                {
                    if (IsTransientSqliteException(ex) && attempt < TransientRetryCount)
                    {
                        _logger.LogWarning(ex, "批量写入压力日志遇到数据库忙，准备第 {Retry} 次重试。", attempt + 1);
                        await Task.Delay(TransientRetryDelayMs * (attempt + 1));
                        continue;
                    }

                    if (IsTransientSqliteException(ex))
                    {
                        _logger.LogError(ex, "批量写入压力日志重试失败，保持数据库模式，不切换降级内存。");
                        throw;
                    }

                    _logger.LogError(ex, "批量写入压力日志失败，切换为降级内存模式。");
                    _useFallback = true;
                    await AddPressureLogsAsync(sessionId, pressureLogs);
                    return;
                }
            }
        }

        public async Task AddEdemaLogsAsync(Guid sessionId, IReadOnlyCollection<EdemaData> edemaLogs)
        {
            if (edemaLogs.Count == 0)
            {
                return;
            }

            foreach (var log in edemaLogs)
            {
                log.SessionId = sessionId;
            }

            if (_useFallback)
            {
                lock (_gate)
                {
                    _fallbackEdemaLogs.AddRange(edemaLogs);
                    var session = _fallbackSessions.FirstOrDefault(x => x.SessionId == sessionId);
                    if (session != null)
                    {
                        foreach (var log in edemaLogs)
                        {
                            session.EdemaLogs.Add(log);
                        }
                    }
                }

                return;
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var context = await _contextFactory.CreateDbContextAsync();
                    context.ChangeTracker.AutoDetectChangesEnabled = false;
                    context.EdemaDataLogs.AddRange(edemaLogs);
                    await context.SaveChangesAsync();

                    lock (_gate)
                    {
                        var session = _fallbackSessions.FirstOrDefault(x => x.SessionId == sessionId);
                        if (session != null)
                        {
                            foreach (var log in edemaLogs)
                            {
                                session.EdemaLogs.Add(log);
                            }
                        }
                    }

                    return;
                }
                catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
                {
                    if (IsTransientSqliteException(ex) && attempt < TransientRetryCount)
                    {
                        _logger.LogWarning(ex, "批量写入水肿数据遇到数据库忙，准备第 {Retry} 次重试。", attempt + 1);
                        await Task.Delay(TransientRetryDelayMs * (attempt + 1));
                        continue;
                    }

                    if (IsTransientSqliteException(ex))
                    {
                        _logger.LogError(ex, "批量写入水肿数据重试失败，保持数据库模式，不切换降级内存。");
                        throw;
                    }

                    _logger.LogError(ex, "批量写入水肿数据失败，切换为降级内存模式。");
                    _useFallback = true;
                    await AddEdemaLogsAsync(sessionId, edemaLogs);
                    return;
                }
            }
        }

        public async Task AddCommandLogsAsync(Guid sessionId, IReadOnlyCollection<CommandLog> commandLogs)
        {
            if (commandLogs.Count == 0)
            {
                return;
            }

            foreach (var log in commandLogs)
            {
                log.SessionId = sessionId;
            }

            if (_useFallback)
            {
                lock (_gate)
                {
                    _fallbackCommandLogs.AddRange(commandLogs);
                    var session = _fallbackSessions.FirstOrDefault(x => x.SessionId == sessionId);
                    if (session != null)
                    {
                        foreach (var log in commandLogs)
                        {
                            session.CommandLogs.Add(log);
                        }
                    }
                }

                return;
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await using var context = await _contextFactory.CreateDbContextAsync();
                    context.ChangeTracker.AutoDetectChangesEnabled = false;
                    context.CommandLogs.AddRange(commandLogs);
                    await context.SaveChangesAsync();

                    lock (_gate)
                    {
                        var session = _fallbackSessions.FirstOrDefault(x => x.SessionId == sessionId);
                        if (session != null)
                        {
                            foreach (var log in commandLogs)
                            {
                                session.CommandLogs.Add(log);
                            }
                        }
                    }

                    return;
                }
                catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
                {
                    if (IsTransientSqliteException(ex) && attempt < TransientRetryCount)
                    {
                        _logger.LogWarning(ex, "批量写入控制指令遇到数据库忙，准备第 {Retry} 次重试。", attempt + 1);
                        await Task.Delay(TransientRetryDelayMs * (attempt + 1));
                        continue;
                    }

                    if (IsTransientSqliteException(ex))
                    {
                        _logger.LogError(ex, "批量写入控制指令重试失败，保持数据库模式，不切换降级内存。");
                        throw;
                    }

                    _logger.LogError(ex, "批量写入控制指令失败，切换为降级内存模式。");
                    _useFallback = true;
                    await AddCommandLogsAsync(sessionId, commandLogs);
                    return;
                }
            }
        }

        public async Task<string?> GetDoctorAdviceAsync()
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackAdvice?.Content;
                }
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                return await context.DoctorAdviceRecords.AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderByDescending(x => x.UpdatedAt)
                    .Select(x => x.Content)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogError(ex, "读取医生建议失败，切换为降级内存模式。");
                _useFallback = true;
                return await GetDoctorAdviceAsync();
            }
        }

        public async Task UpdateDoctorAdviceAsync(string advice)
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    _fallbackAdvice = new DoctorAdviceRecord
                    {
                        Id = Guid.NewGuid(),
                        Content = advice,
                        UpdatedAt = DateTime.Now,
                        IsActive = true
                    };
                }

                return;
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var activeAdvice = await context.DoctorAdviceRecords.FirstOrDefaultAsync(x => x.IsActive);
                if (activeAdvice == null)
                {
                    context.DoctorAdviceRecords.Add(new DoctorAdviceRecord
                    {
                        Id = Guid.NewGuid(),
                        Content = advice,
                        UpdatedAt = DateTime.Now,
                        IsActive = true
                    });
                }
                else
                {
                    activeAdvice.Content = advice;
                    activeAdvice.UpdatedAt = DateTime.Now;
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogError(ex, "更新医生建议失败，切换为降级内存模式。");
                _useFallback = true;
                await UpdateDoctorAdviceAsync(advice);
            }
        }

        public async Task<IReadOnlyList<TreatmentReminder>> GetPendingRemindersAsync(DateTime? asOf = null)
        {
            var now = asOf ?? DateTime.Now;

            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackReminders
                        .Where(x => !x.IsCompleted && x.DueAt <= now)
                        .OrderBy(x => x.DueAt)
                        .ToList();
                }
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                return await context.TreatmentReminders.AsNoTracking()
                    .Where(x => !x.IsCompleted && x.DueAt <= now)
                    .OrderBy(x => x.DueAt)
                    .ToListAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogError(ex, "查询提醒失败，切换为降级内存模式。");
                _useFallback = true;
                return await GetPendingRemindersAsync(asOf);
            }
        }

        public async Task UpdateReminderAsync(TreatmentReminder reminder)
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    _fallbackReminders.RemoveAll(x => x.ReminderId == reminder.ReminderId);
                    _fallbackReminders.Add(reminder);
                }

                return;
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                var existing = await context.TreatmentReminders.FirstOrDefaultAsync(x => x.ReminderId == reminder.ReminderId);
                if (existing == null)
                {
                    context.TreatmentReminders.Add(reminder);
                }
                else
                {
                    existing.Content = reminder.Content;
                    existing.DueAt = reminder.DueAt;
                    existing.IsCompleted = reminder.IsCompleted;
                    existing.CreatedAt = reminder.CreatedAt;
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogError(ex, "更新提醒失败，切换为降级内存模式。");
                _useFallback = true;
                await UpdateReminderAsync(reminder);
            }
        }
    }
}
