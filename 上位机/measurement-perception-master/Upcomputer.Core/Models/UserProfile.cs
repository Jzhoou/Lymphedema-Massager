using System;
using System.Collections.Generic;
using Upcomputer.Common.Models;

namespace Upcomputer.Core.Models
{
    /// <summary>
    /// 用户（患者）档案
    /// <para>存储患者的基本信息、体征数据及关联的治疗会话列表。
    /// 支持数据绑定，身高/体重变化时自动重算 BMI。</para>
    /// </summary>
    public class UserProfile : ObservableObject
    {
        private string _userId = string.Empty;
        private string _userName = string.Empty;
        private string? _gender;
        private DateTime? _birthDate;
        private string? _phoneNumber;
        private double? _heightCm;
        private double? _weightKg;
        private DateTime _createdAt;

        /// <summary>用户唯一标识（字符串类型，便于自定义编号）</summary>
        public string UserId
        {
            get => _userId;
            set => SetField(ref _userId, value);
        }

        /// <summary>用户姓名</summary>
        public string UserName
        {
            get => _userName;
            set => SetField(ref _userName, value);
        }

        /// <summary>性别</summary>
        public string? Gender
        {
            get => _gender;
            set => SetField(ref _gender, value);
        }

        /// <summary>出生日期</summary>
        public DateTime? BirthDate
        {
            get => _birthDate;
            set => SetField(ref _birthDate, value);
        }

        /// <summary>联系电话</summary>
        public string? PhoneNumber
        {
            get => _phoneNumber;
            set => SetField(ref _phoneNumber, value);
        }

        /// <summary>身高（厘米），变更时自动触发 BMI 重算</summary>
        public double? HeightCm
        {
            get => _heightCm;
            set
            {
                if (SetField(ref _heightCm, value))
                {
                    OnPropertyChanged(nameof(Bmi));
                    OnPropertyChanged(nameof(BmiDisplay));
                }
            }
        }

        /// <summary>体重（千克），变更时自动触发 BMI 重算</summary>
        public double? WeightKg
        {
            get => _weightKg;
            set
            {
                if (SetField(ref _weightKg, value))
                {
                    OnPropertyChanged(nameof(Bmi));
                    OnPropertyChanged(nameof(BmiDisplay));
                }
            }
        }

        /// <summary>
        /// 体质指数（BMI = 体重kg / 身高m²）
        /// <para>身高或体重未填写时返回 <c>null</c>。</para>
        /// </summary>
        public double? Bmi
        {
            get
            {
                if (HeightCm is null || WeightKg is null)
                {
                    return null;
                }

                var heightM = HeightCm.Value / 100.0;
                if (heightM <= 0)
                {
                    return null;
                }

                return WeightKg.Value / (heightM * heightM);
            }
        }

        /// <summary>BMI 显示文本，未计算时显示 "-"</summary>
        public string BmiDisplay => Bmi is null ? "-" : Bmi.Value.ToString("F1");

        /// <summary>档案创建时间</summary>
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetField(ref _createdAt, value);
        }

        /// <summary>该用户关联的所有治疗会话</summary>
        public ICollection<TreatmentSession> TreatmentSessions { get; set; } = new List<TreatmentSession>();
    }
}