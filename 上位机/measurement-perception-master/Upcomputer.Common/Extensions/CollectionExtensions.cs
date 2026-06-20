using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Upcomputer.Common.Extensions
{
    /// <summary>
    /// 集合扩展方法类
    /// <para>为 <see cref="ObservableCollection{T}"/> 等集合类型提供常用的批量操作扩展。</para>
    /// </summary>
    public static class CollectionExtensions
    {
        /// <summary>
        /// 向 <see cref="ObservableCollection{T}"/> 批量添加元素
        /// <para>逐项调用 <c>Add</c>，确保每次添加都能触发 <c>CollectionChanged</c> 事件，使 UI 绑定自动刷新。</para>
        /// </summary>
        /// <typeparam name="T">集合元素类型</typeparam>
        /// <param name="collection">目标可观察集合</param>
        /// <param name="items">待添加的元素序列</param>
        public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                collection.Add(item);
            }
        }
    }
}