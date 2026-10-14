using System;
using System.Collections.Generic;
using System.Text;
using Upcomputer.Core.Models;

namespace Upcomputer.Core.Interfaces
{
    /// <summary>
    /// 治疗会话仓储接口
    /// <para>定义治疗会话、压力日志、水肿日志、命令日志、医生建议及提醒的持久化操作。</para>
    /// </summary>
    public interface ITreatmentRepository
    {
        /// <summary>
        /// 创建并启动一个新的治疗会话
        /// </summary>
        /// <param name="session">待创建的治疗会话实体</param>
        /// <returns>新会话的唯一标识 <see cref="Guid"/></returns>
        Task<Guid> StartTreatmentSessionAsync(TreatmentSession session);

        /// <summary>
        /// 结束指定治疗会话
        /// </summary>
        /// <param name="sessionId">会话唯一标识</param>
        /// <param name="endTime">会话结束时间</param>
        Task EndTreatmentSessionAsync(Guid sessionId, DateTime endTime);

        /// <summary>
        /// 统计指定时间范围内的会话数量
        /// </summary>
        /// <param name="startDate">起始时间（含）</param>
        /// <param name="endDate">截止时间（含）</param>
        /// <returns>会话数量</returns>
        Task<int> GetSessionCountAsync(DateTime startDate, DateTime endDate);

        /// <summary>
        /// 分页查询指定时间范围内的会话列表（按开始时间倒序）
        /// </summary>
        /// <param name="startDate">起始时间（含）</param>
        /// <param name="endDate">截止时间（含）</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页记录数</param>
        /// <returns>会话列表</returns>
        Task<IReadOnlyList<TreatmentSession>> GetSessionsAsync(DateTime startDate, DateTime endDate, int pageIndex = 1, int pageSize = 20);

        /// <summary>
        /// 获取会话详情（含关联的用户信息和命令日志）
        /// </summary>
        /// <param name="sessionId">会话唯一标识</param>
        /// <returns>会话实体，未找到返回 <c>null</c></returns>
        Task<TreatmentSession?> GetSessionDetailsAsync(Guid sessionId);

        /// <summary>统计指定会话的压力日志数量</summary>
        Task<int> GetPressureLogCountAsync(Guid sessionId);

        /// <summary>分页获取指定会话的压力日志</summary>
        Task<IReadOnlyList<PressureData>> GetPressureLogsAsync(Guid sessionId, int pageIndex = 1, int pageSize = 60);

        /// <summary>统计指定会话的水肿日志数量</summary>
        Task<int> GetEdemaLogCountAsync(Guid sessionId);

        /// <summary>分页获取指定会话的水肿日志</summary>
        Task<IReadOnlyList<EdemaData>> GetEdemaLogsAsync(Guid sessionId, int pageIndex = 1, int pageSize = 60);

        /// <summary>统计指定会话的命令日志数量</summary>
        Task<int> GetCommandLogCountAsync(Guid sessionId);

        /// <summary>分页获取指定会话的命令日志</summary>
        Task<IReadOnlyList<CommandLog>> GetCommandLogsAsync(Guid sessionId, int pageIndex = 1, int pageSize = 60);

        /// <summary>
        /// 删除指定治疗会话及其所有关联日志
        /// </summary>
        /// <param name="sessionId">会话唯一标识</param>
        /// <returns>删除成功返回 <c>true</c>，会话不存在返回 <c>false</c></returns>
        Task<bool> DeleteTreatmentSessionAsync(Guid sessionId);

        /// <summary>批量写入压力日志</summary>
        Task AddPressureLogsAsync(Guid sessionId, IReadOnlyCollection<PressureData> pressureLogs);

        /// <summary>批量写入水肿日志</summary>
        Task AddEdemaLogsAsync(Guid sessionId, IReadOnlyCollection<EdemaData> edemaLogs);

        /// <summary>批量写入命令日志</summary>
        Task AddCommandLogsAsync(Guid sessionId, IReadOnlyCollection<CommandLog> commandLogs);

        /// <summary>获取当前有效的医生建议内容</summary>
        /// <returns>建议文本，无建议时返回 <c>null</c></returns>
        Task<string?> GetDoctorAdviceAsync();

        /// <summary>更新或新增医生建议</summary>
        /// <param name="advice">新的建议内容</param>
        Task UpdateDoctorAdviceAsync(string advice);

        /// <summary>
        /// 获取到期但未完成的提醒列表
        /// </summary>
        /// <param name="asOf">截止时间点，默认当前时间</param>
        /// <returns>待处理提醒列表</returns>
        Task<IReadOnlyList<TreatmentReminder>> GetPendingRemindersAsync(DateTime? asOf = null);

        /// <summary>更新或新增一条提醒</summary>
        Task UpdateReminderAsync(TreatmentReminder reminder);
    }

    /// <summary>
    /// 压力数据日志仓储接口
    /// <para>用于实时压力数据的独立持久化（区别于会话关联的批量写入）。</para>
    /// </summary>
    public interface IPressureLogRepository
    {
        /// <summary>保存单条压力数据记录</summary>
        Task SavePressureDataAsync(PressureData data);

        /// <summary>批量保存压力数据记录</summary>
        Task SavePressureDataBatchAsync(IReadOnlyCollection<PressureData> data);

        /// <summary>
        /// 按时间范围查询压力数据
        /// </summary>
        /// <param name="startDate">起始时间（含）</param>
        /// <param name="endDate">截止时间（含）</param>
        /// <returns>压力数据列表</returns>
        Task<List<PressureData>> GetPressureDataAsync(DateTime startDate, DateTime endDate);
    }
}