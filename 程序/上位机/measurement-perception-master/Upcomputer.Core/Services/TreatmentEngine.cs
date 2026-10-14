using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;
using Upcomputer.Core.Models;
using Upcomputer.Common.Enums;
using Upcomputer.Core.Interfaces;

namespace Upcomputer.Core.Services
{
    /// <summary>
    /// 治疗引擎
    /// <para>
    /// 管理治疗会话的生命周期（启动、暂停、恢复、停止），
    /// 内部使用 1 秒周期定时器驱动治疗进度递增，到达总时长后自动停止并触发完成事件。
    /// 支持通过 <see cref="IUserSelected"/> 事件总线动态切换当前操作用户。
    /// </para>
    /// </summary>
    public class TreatmentEngine : IDisposable
    {
        /// <summary>治疗计时器（1 秒周期）</summary>
        private readonly System.Timers.Timer _treatmentTimer;

        /// <summary>当前活跃的治疗会话</summary>
        private TreatmentSession _currentSession = new();

        /// <summary>线程同步锁</summary>
        private readonly object _lock = new();

        /// <summary>用户选择事件总线（可选）</summary>
        private readonly IUserSelected? _userSelected;
        private string _selectedUserId = "admin";
        private UserProfile? _selectedUser;

        /// <summary>治疗会话状态更新事件（每秒触发一次，或状态变化时触发）</summary>
        public event EventHandler<TreatmentSession> SessionUpdated = null!;

        /// <summary>治疗完成事件（到达总时长时触发）</summary>
        public event EventHandler TreatmentCompleted = null!;

        /// <summary>
        /// 创建治疗引擎实例
        /// </summary>
        /// <param name="userSelected">用户选择事件总线，为 <c>null</c> 时不订阅用户切换</param>
        public TreatmentEngine(IUserSelected? userSelected = null)
        {
            _userSelected = userSelected;
            if (_userSelected != null)
            {
                _userSelected.UserSelected += OnUserSelected;
            }

            // 1秒周期定时器，用于驱动治疗进度递增
            _treatmentTimer = new System.Timers.Timer(1000);
            _treatmentTimer.Elapsed += OnTreatmentTimerElapsed;
        }

        /// <summary>
        /// 用户切换事件处理 —— 更新当前会话的用户关联
        /// </summary>
        private void OnUserSelected(object? sender, UserSelectedEventArgs e)
        {
            lock (_lock)
            {
                _selectedUserId = string.IsNullOrWhiteSpace(e.User.UserId) ? "admin" : e.User.UserId;
                _selectedUser = e.User;
                CurrentSession.UserId = _selectedUserId;
                CurrentSession.User = _selectedUser;
            }

            OnSessionUpdated();
        }

        /// <summary>当前治疗会话（线程安全访问）</summary>
        public TreatmentSession CurrentSession
        {
            get { lock (_lock) return _currentSession; }
            private set { lock (_lock) _currentSession = value; }
        }

        /// <summary>
        /// 启动新的治疗会话
        /// <para>会重置之前的会话状态并启动计时器。时长参数单位为分钟，内部转换为秒存储。</para>
        /// </summary>
        /// <param name="mode">治疗模式</param>
        /// <param name="duration">治疗时长（单位：分钟），内部乘以 60 转换为秒</param>
        /// <param name="intensity">按摩强度（0-100%）</param>
        public void StartTreatment(TherapyMode mode, int duration, double intensity)
        {
            UserProfile? selectedUser;
            string selectedUserId;

            lock (_lock)
            {
                selectedUser = _selectedUser;
                selectedUserId = _selectedUserId;
            }

            CurrentSession = new TreatmentSession
            {
                Mode = mode,
                TotalDuration = duration * 60, // 分钟转秒
                ElapsedTime = 0,
                Intensity = intensity,
                IsRunning = true,
                StartTime = DateTime.Now,
                EndTime = null,
                UserId = selectedUserId,
                User = selectedUser
            };

            _treatmentTimer.Start();
            OnSessionUpdated();
        }

        /// <summary>
        /// 停止当前治疗会话
        /// <para>停止计时器，标记会话结束并记录结束时间。</para>
        /// </summary>
        public void StopTreatment()
        {
            _treatmentTimer.Stop();
            CurrentSession.IsRunning = false;
            CurrentSession.EndTime = DateTime.Now;
            OnSessionUpdated();
        }

        /// <summary>
        /// 暂停当前治疗（计时器暂停，可恢复）
        /// </summary>
        public void PauseTreatment()
        {
            _treatmentTimer.Stop();
            CurrentSession.IsRunning = false;
            OnSessionUpdated();
        }

        /// <summary>
        /// 恢复已暂停的治疗（计时器继续）
        /// </summary>
        public void ResumeTreatment()
        {
            CurrentSession.IsRunning = true;
            _treatmentTimer.Start();
            OnSessionUpdated();
        }

        /// <summary>
        /// 定时器回调 —— 每秒递增已用时间，到达总时长时自动停止
        /// </summary>
        private void OnTreatmentTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            lock (_lock)
            {
                if (!CurrentSession.IsRunning) return;

                CurrentSession.ElapsedTime++;
                OnSessionUpdated();

                // 到达总时长，自动结束治疗
                if (CurrentSession.ElapsedTime >= CurrentSession.TotalDuration)
                {
                    _treatmentTimer.Stop();
                    CurrentSession.IsRunning = false;
                    CurrentSession.EndTime = DateTime.Now;
                    TreatmentCompleted?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>触发会话更新事件</summary>
        private void OnSessionUpdated()
        {
            SessionUpdated?.Invoke(this, CurrentSession);
        }

        /// <summary>
        /// 释放资源（取消事件订阅、释放定时器）
        /// </summary>
        public void Dispose()
        {
            if (_userSelected != null)
            {
                _userSelected.UserSelected -= OnUserSelected;
            }
            _treatmentTimer?.Dispose();
        }
    }
}