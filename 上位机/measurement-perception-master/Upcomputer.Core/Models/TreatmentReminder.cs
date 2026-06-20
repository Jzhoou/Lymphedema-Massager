using System;
using Upcomputer.Common.Models;

namespace Upcomputer.Core.Models
{
    /// <summary>
    /// 治疗提醒实体
    /// <para>记录需要提醒用户的康复训练事项，支持到期提醒和完成状态跟踪。</para>
    /// </summary>
    public class TreatmentReminder : ObservableObject
    {
        private Guid _reminderId;
        private string _content = string.Empty;
        private DateTime _dueAt;
        private bool _isCompleted;
        private DateTime _createdAt;

        /// <summary>提醒唯一标识</summary>
        public Guid ReminderId
        {
            get => _reminderId;
            set => SetField(ref _reminderId, value);
        }

        /// <summary>提醒内容描述</summary>
        public string Content
        {
            get => _content;
            set => SetField(ref _content, value);
        }

        /// <summary>提醒到期时间</summary>
        public DateTime DueAt
        {
            get => _dueAt;
            set => SetField(ref _dueAt, value);
        }

        /// <summary>是否已完成</summary>
        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetField(ref _isCompleted, value);
        }

        /// <summary>创建时间</summary>
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetField(ref _createdAt, value);
        }
    }
}