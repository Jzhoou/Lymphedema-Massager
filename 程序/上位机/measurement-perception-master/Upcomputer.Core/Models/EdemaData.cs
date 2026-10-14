using System;
using System.Collections.Generic;
using System.Text;
using Upcomputer.Common.Models;

namespace Upcomputer.Core.Models
{
    /// <summary>
    /// 水肿/生物阻抗数据实体
    /// <para>记录单路生物阻抗传感器的测量值，支持数据绑定和阈值告警。
    /// 当 <see cref="EdemaPercentage"/> 超过 <see cref="Threshold"/> 时自动设置 <see cref="IsAlert"/>。</para>
    /// </summary>
    public class EdemaData : ObservableObject
    {
        private long _id;
        private double _impedance;
        private double _edemaPercentage;
        private double _threshold;
        private bool _isAlert;
        private DateTime _timestamp;
        private double _latencyMs;

        /// <summary>自增主键</summary>
        public long Id
        {
            get => _id;
            set => SetField(ref _id, value);
        }

        /// <summary>所属治疗会话唯一标识</summary>
        public Guid SessionId { get; set; }

        /// <summary>所属治疗会话导航属性</summary>
        public TreatmentSession? Session { get; set; }

        /// <summary>传感器编号（1-2）</summary>
        public int SensorId { get; set; }

        /// <summary>
        /// 生物阻抗值（单位：Ω）
        /// <para>变更时自动更新采集时间戳。</para>
        /// </summary>
        public double Impedance
        {
            get => _impedance;
            set
            {
                if (SetField(ref _impedance, value))
                {
                    Timestamp = DateTime.Now;
                    OnPropertyChanged(nameof(DisplayImpedance));
                }
            }
        }

        /// <summary>
        /// 水肿百分比（单位：%）
        /// <para>变更时自动与阈值比较触发告警。</para>
        /// </summary>
        public double EdemaPercentage
        {
            get => _edemaPercentage;
            set
            {
                if (SetField(ref _edemaPercentage, value))
                {
                    // 自动检测是否超过告警阈值
                    IsAlert = value > Threshold;
                    OnPropertyChanged(nameof(DisplayPercentage));
                }
            }
        }

        /// <summary>告警阈值（单位：%）</summary>
        public double Threshold
        {
            get => _threshold;
            set => SetField(ref _threshold, value);
        }

        /// <summary>是否处于告警状态</summary>
        public bool IsAlert
        {
            get => _isAlert;
            set => SetField(ref _isAlert, value);
        }

        /// <summary>数据采集时间戳</summary>
        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetField(ref _timestamp, value);
        }

        /// <summary>单包数据传输延迟（毫秒），由协议时间戳计算得出</summary>
        public double LatencyMs
        {
            get => _latencyMs;
            set => SetField(ref _latencyMs, value);
        }

        /// <summary>格式化的阻抗显示文本（如 "500 Ω"）</summary>
        public string DisplayImpedance => $"{Impedance:F0} Ω";

        /// <summary>格式化的水肿百分比显示文本（如 "12.5%"）</summary>
        public string DisplayPercentage => $"{EdemaPercentage:F1}%";
    }
}