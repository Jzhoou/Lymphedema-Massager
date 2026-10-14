using System;
using System.Collections.Generic;
using System.Text;
using Upcomputer.Common.Enums;
using Upcomputer.Common.Models;

namespace Upcomputer.Core.Models
{
    /// <summary>
    /// 步进电机状态实体
    /// <para>实时反映步进电机的运行状态、转速、方向和当前位置，支持数据绑定。
    /// 状态、转速、方向等属性变更时自动触发对应显示文本的刷新。</para>
    /// </summary>
    public class StepperMotorStatus : ObservableObject
    {
        private MotorState _state;
        private double _speed;
        private bool _direction;
        private double _currentAngle;
        private bool _isConnected;

        /// <summary>电机编号（1 或 2）</summary>
        public int MotorId { get; set; }

        /// <summary>
        /// 电机运行状态
        /// <para>变更时自动刷新 <see cref="StateDisplay"/>。</para>
        /// </summary>
        public MotorState State
        {
            get => _state;
            set
            {
                if (SetField(ref _state, value))
                {
                    OnPropertyChanged(nameof(StateDisplay));
                }
            }
        }

        /// <summary>
        /// 电机转速（单位：rpm）
        /// <para>变更时自动刷新 <see cref="SpeedDisplay"/>。</para>
        /// </summary>
        public double Speed
        {
            get => _speed;
            set
            {
                if (SetField(ref _speed, value))
                {
                    OnPropertyChanged(nameof(SpeedDisplay));
                }
            }
        }

        /// <summary>
        /// 旋转方向（<c>true</c> = 正转，<c>false</c> = 反转）
        /// </summary>
        public bool Direction
        {
            get => _direction;
            set
            {
                if (SetField(ref _direction, value))
                {
                    OnPropertyChanged(nameof(DirectionDisplay));
                }
            }
        }

        /// <summary>当前角度位置（单位：度）</summary>
        public double CurrentAngle
        {
            get => _currentAngle;
            set => SetField(ref _currentAngle, value);
        }

        /// <summary>
        /// 设备是否已连接
        /// <para>变更时自动刷新 <see cref="ConnectionStatusDisplay"/>。</para>
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetField(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(ConnectionStatusDisplay));
                }
            }
        }

        /// <summary>设备名称显示文本（如 "步进电机 1"）</summary>
        public string DeviceName => $"步进电机 {MotorId}";

        /// <summary>当前值显示文本（别名，指向 <see cref="SpeedDisplay"/>）</summary>
        public string CurrentValueDisplay => SpeedDisplay;

        /// <summary>连接状态显示文本（"已连接" / "未连接"）</summary>
        public string ConnectionStatusDisplay => IsConnected ? "已连接" : "未连接";

        /// <summary>状态显示文本（"Stopped" / "Running" / "Error"）</summary>
        public string StateDisplay => State.ToString();

        /// <summary>方向显示文本（"正转" / "反转"）</summary>
        public string DirectionDisplay => Direction ? "正转" : "反转";

        /// <summary>转速格式化显示文本（如 "120 rpm"）</summary>
        public string SpeedDisplay => $"{Speed:F0} rpm";
    }

    /// <summary>
    /// 舵机状态实体
    /// <para>实时反映舵机的角度位置、限位状态和归位状态，支持数据绑定。</para>
    /// </summary>
    public class ServoStatus : ObservableObject
    {
        private double _angle;
        private bool _isAtLimit;
        private bool _isHomed;
        private bool _isConnected;

        /// <summary>舵机编号（1-6）</summary>
        public int ServoId { get; set; }

        /// <summary>
        /// 当前角度（单位：度，范围 0-180）
        /// <para>变更时自动刷新 <see cref="AngleDisplay"/> 和 <see cref="CurrentValueDisplay"/>。</para>
        /// </summary>
        public double Angle
        {
            get => _angle;
            set
            {
                if (SetField(ref _angle, value))
                {
                    OnPropertyChanged(nameof(AngleDisplay));
                    OnPropertyChanged(nameof(CurrentValueDisplay));
                }
            }
        }

        /// <summary>
        /// 设备是否已连接
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetField(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(ConnectionStatusDisplay));
                }
            }
        }

        /// <summary>是否到达限位</summary>
        public bool IsAtLimit
        {
            get => _isAtLimit;
            set => SetField(ref _isAtLimit, value);
        }

        /// <summary>是否已完成归位</summary>
        public bool IsHomed
        {
            get => _isHomed;
            set => SetField(ref _isHomed, value);
        }

        /// <summary>设备名称显示文本（如 "舵机 1"）</summary>
        public string DeviceName => $"舵机 {ServoId}";

        /// <summary>当前值显示文本（别名，指向 <see cref="AngleDisplay"/>）</summary>
        public string CurrentValueDisplay => AngleDisplay;

        /// <summary>连接状态显示文本（"已连接" / "未连接"）</summary>
        public string ConnectionStatusDisplay => IsConnected ? "已连接" : "未连接";

        /// <summary>角度格式化显示文本（如 "90.0°"）</summary>
        public string AngleDisplay => $"{Angle:F1}°";
    }
}