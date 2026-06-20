using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Upcomputer.Core.Interfaces;
using Upcomputer.Core.Models;
using Upcomputer.Data.Database;

namespace Upcomputer.Data.Repositories
{
    /// <summary>
    /// 压力数据日志仓储实现
    /// <para>
    /// 实现 <see cref="IPressureLogRepository"/> 接口，提供压力数据的持久化和查询。
    /// 当数据库操作失败时自动切换为降级内存模式（<c>_useFallback = true</c>），确保应用不会因数据库异常而崩溃。
    /// </para>
    /// </summary>
    public class PressureLogRepository : IPressureLogRepository
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ILogger<PressureLogRepository> _logger;
        private readonly List<PressureData> _fallbackPressureLogs = new();
        private bool _useFallback;
        private readonly object _gate = new();

        public PressureLogRepository(IDbContextFactory<AppDbContext> contextFactory, ILogger<PressureLogRepository> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task SavePressureDataAsync(PressureData data)
        {
            await SavePressureDataBatchAsync(new[] { data });
        }

        public async Task SavePressureDataBatchAsync(IReadOnlyCollection<PressureData> data)
        {
            if (data.Count == 0)
            {
                return;
            }

            if (_useFallback)
            {
                lock (_gate)
                {
                    _fallbackPressureLogs.AddRange(data);
                }

                return;
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                context.ChangeTracker.AutoDetectChangesEnabled = false;
                context.PressureDataLogs.AddRange(data);
                await context.SaveChangesAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "保存压力日志失败，切换为降级内存模式。");
                _useFallback = true;
                await SavePressureDataBatchAsync(data);
            }
        }

        public async Task<List<PressureData>> GetPressureDataAsync(DateTime startDate, DateTime endDate)
        {
            if (_useFallback)
            {
                lock (_gate)
                {
                    return _fallbackPressureLogs.Where(x => x.Timestamp >= startDate && x.Timestamp <= endDate).ToList();
                }
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                return await context.PressureDataLogs.AsNoTracking()
                    .Where(x => x.Timestamp >= startDate && x.Timestamp <= endDate)
                    .OrderByDescending(x => x.Timestamp)
                    .ToListAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or SqliteException or InvalidOperationException)
            {
                _logger.LogError(ex, "查询压力日志失败，切换为降级内存模式。");
                _useFallback = true;
                return await GetPressureDataAsync(startDate, endDate);
            }
        }
    }
}
