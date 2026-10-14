using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upcomputer.Common.Models;
using Upcomputer.Common.Enums;

namespace Upcomputer.Core.Models
{
    /// <summary>
    /// 治疗会话实体
    /// <para>记录一次完整的治疗过程，包括治疗模式、强度、时长、进度等。
    /// 支持数据绑定，<see cref="ElapsedTime"/> 变化时自动计算进度百分比和剩余时间。</para>
    /// </summary>
    public class TreatmentSession : ObservableObject
    {
        private TherapyMode _mode;
        private int _totalDuration;
        private int _elapsedTime;
        private double _intensity;
        private bool _isRunning;
        private double _progress;
        private DateTime _startTime;
        private DateTime? _endTime;
        private string? _doctorAdvice;
        private string _userId = "admin";

        /// <summary>会话唯一标识</summary>
        public Guid SessionId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 所属用户标识
        /// <para>空值或空白时自动回退为 "admin"（默认用户）。</para>
        /// </summary>
        public string UserId
        {
            get => _userId;
            set => SetField(ref _userId, string.IsNullOrWhiteSpace(value) ? "admin" : value);
        }

        /// <summary>所属用户档案导航属性</summary>
        public UserProfile? User { get; set; }

        /// <summary>会话结束时间（未结束时为 <c>null</c>）</summary>
        public DateTime? EndTime
        {
            get => _endTime;
            set => SetField(ref _endTime, value);
        }

        /// <summary>医生在治疗期间给出的建议文本</summary>
        public string? DoctorAdvice
        {
            get => _doctorAdvice;
            set => SetField(ref _doctorAdvice, value);
        }

        /// <summary>本次会话关联的压力数据日志</summary>
        public ICollection<PressureData> PressureLogs { get; set; } = new List<PressureData>();

        /// <summary>本次会话关联的水肿数据日志</summary>
        public ICollection<EdemaData> EdemaLogs { get; set; } = new List<EdemaData>();

        /// <summary>本次会话关联的命令操作日志</summary>
        public ICollection<CommandLog> CommandLogs { get; set; } = new List<CommandLog>();

        /// <summary>
        /// 治疗模式
        /// <para>变更时自动刷新 <see cref="ModeDisplay"/>。</para>
        /// </summary>
        public TherapyMode Mode
        {
            get => _mode;
            set
            {
                if (SetField(ref _mode, value))
                {
                    OnPropertyChanged(nameof(ModeDisplay));
                }
            }
        }

        /// <summary>
        /// 治疗总时长（单位：秒）
        /// <para>变更时自动刷新剩余时间相关属性。</para>
        /// </summary>
        public int TotalDuration
        {
            get => _totalDuration;
            set
            {
                if (SetField(ref _totalDuration, value))
                {
                    OnPropertyChanged(nameof(RemainingTime));
                    OnPropertyChanged(nameof(RemainingTimeDisplay));
                }
            }
        }

        /// <summary>
        /// 已用时间（单位：秒）
        /// <para>变更时自动计算进度百分比，并刷新剩余时间和已用时间的显示文本。</para>
        /// </summary>
        public int ElapsedTime
        {
            get => _elapsedTime;
            set
            {
                if (SetField(ref _elapsedTime, value))
                {
                    // 根据已用时间自动计算进度百分比
                    Progress = TotalDuration > 0 ? (double)ElapsedTime / TotalDuration * 100 : 0;
                    OnPropertyChanged(nameof(RemainingTime));
                    OnPropertyChanged(nameof(RemainingTimeDisplay));
                    OnPropertyChanged(nameof(ElapsedTimeDisplay));
                }
            }
        }

        /// <summary>按摩强度（0-100%）</summary>
        public double Intensity
        {
            get => _intensity;
            set => SetField(ref _intensity, value);
        }

        /// <summary>治疗是否正在进行</summary>
        public bool IsRunning
        {
            get => _isRunning;
            set => SetField(ref _isRunning, value);
        }

        /// <summary>治疗进度百分比（0-100）</summary>
        public double Progress
        {
            get => _progress;
            set => SetField(ref _progress, value);
        }

        /// <summary>治疗开始时间</summary>
        public DateTime StartTime
        {
            get => _startTime;
            set => SetField(ref _startTime, value);
        }

        /// <summary>剩余时间（单位：秒），最小值为 0</summary>
        public int RemainingTime => Math.Max(0, TotalDuration - ElapsedTime);

        /// <summary>剩余时间格式化显示（如 "02:30" 表示 2 分 30 秒）</summary>
        public string RemainingTimeDisplay => $"{RemainingTime / 60:D2}:{RemainingTime % 60:D2}";

        /// <summary>已用时间格式化显示（如 "01:15" 表示 1 分 15 秒）</summary>
        public string ElapsedTimeDisplay => $"{ElapsedTime / 60:D2}:{ElapsedTime % 60:D2}";

        /// <summary>治疗模式中文显示文本</summary>
        public string ModeDisplay => Mode switch
        {
            TherapyMode.Rehabilitation => "康复模式",
            TherapyMode.Massage => "按摩模式",
            TherapyMode.Custom => "自定义模式",
            _ => "未知"
        };
    }
}