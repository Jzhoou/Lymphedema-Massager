using System;
using Upcomputer.Common.Models;

namespace Upcomputer.Core.Models
{
    /// <summary>
    /// 医生建议记录
    /// <para>存储医生给出的康复训练建议，支持实时更新和 UI 绑定。</para>
    /// </summary>
    public class DoctorAdviceRecord : ObservableObject
    {
        private Guid _id;
        private string _content = string.Empty;
        private DateTime _updatedAt;
        private bool _isActive;

        /// <summary>记录唯一标识</summary>
        public Guid Id
        {
            get => _id;
            set => SetField(ref _id, value);
        }

        /// <summary>建议内容文本</summary>
        public string Content
        {
            get => _content;
            set => SetField(ref _content, value);
        }

        /// <summary>最后更新时间</summary>
        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set => SetField(ref _updatedAt, value);
        }

        /// <summary>是否为当前有效建议（同一时间仅一条生效）</summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetField(ref _isActive, value);
        }
    }
}