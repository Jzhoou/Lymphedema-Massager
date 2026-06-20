using System;
using System.Collections.Generic;
using System.Text;
using Upcomputer.Common.Models;

namespace Upcomputer.Core.Models
{
    /// <summary>
    /// 压力传感器数据实体
    /// <para>记录单路压力传感器的实时测量值，支持数据绑定和阈值告警。
    /// 当 <see cref="CurrentValue"/> 超过 <see cref="Threshold"/> 时自动设置 <see cref="IsAlert"/>。</para>
    /// </summary>
    public class PressureData : ObservableObject
    {
        private long _id;
        private double _currentValue;
        private double _maxValue;
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

        /// <summary>传感器编号（1-6）</summary>
        public int SensorId { get; set; }

        /// <summary>
        /// 当前压力值（单位：g）
        /// <para>变更时自动更新时间戳、显示文本，并与阈值比较触发告警。</para>
        /// </summary>
        public double CurrentValue
        {
            get => _currentValue;
            set
            {
                if (SetField(ref _currentValue, value))
                {
                    // 自动检测是否超过告警阈值
                    IsAlert = value > Threshold;
                    Timestamp = DateTime.Now;
                    OnPropertyChanged(nameof(DisplayValue));
                }
            }
        }

        /// <summary>历史最大压力值（单位：g）</summary>
        public double MaxValue
        {
            get => _maxValue;
            set
            {
                if (SetField(ref _maxValue, value))
                {
                    OnPropertyChanged(nameof(DisplayMax));
                }
            }
        }

        /// <summary>
        /// 告警阈值（单位：g）
        /// <para>变更时重新评估当前值是否触发告警。</para>
        /// </summary>
        public double Threshold
        {
            get => _threshold;
            set
            {
                if (SetField(ref _threshold, value))
                {
                    IsAlert = CurrentValue > value;
                }
            }
        }

        /// <summary>是否处于告警状态（当前值超过阈值）</summary>
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

        /// <summary>格式化的当前值显示文本（如 "12.3 g"）</summary>
        public string DisplayValue => $"{CurrentValue:F1} g";

        /// <summary>格式化的最大值显示文本（如 "45.6 g"）</summary>
        public string DisplayMax => $"{MaxValue:F1} g";
    }
}