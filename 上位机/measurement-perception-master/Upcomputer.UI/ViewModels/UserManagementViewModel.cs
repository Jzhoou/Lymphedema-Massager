using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Upcomputer.Common.Models;
using Upcomputer.Core.Models;
using Upcomputer.Data.Database;

namespace Upcomputer.UI.ViewModels
{
    public class UserManagementViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        private readonly ObservableCollection<UserProfile> _users = new();
        private string _searchText = string.Empty;
        private UserProfile? _selectedUser;
        private string _statusText = "就绪";

        private int _currentPage = 1;
        private int _totalPages = 1;
        private int _totalItems;

        private const int PageSize = 20;

        public UserManagementViewModel(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;

            SearchCommand = new AsyncRelayCommand(LoadFirstPageAsync);
            AddUserCommand = new AsyncRelayCommand(AddUserAsync);
            DeleteUserCommand = new AsyncRelayCommand(DeleteSelectedUserAsync, CanDeleteUser);
            PreviousPageCommand = new AsyncRelayCommand(LoadPreviousPageAsync, () => CurrentPage > 1);
            NextPageCommand = new AsyncRelayCommand(LoadNextPageAsync, () => CurrentPage < TotalPages);

            _ = LoadFirstPageAsync();
        }

        public ObservableCollection<UserProfile> Users => _users;

        public string SearchText
        {
            get => _searchText;
            set => SetField(ref _searchText, value ?? string.Empty);
        }

        public UserProfile? SelectedUser
        {
            get => _selectedUser;
            set
            {
                if (SetField(ref _selectedUser, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public int CurrentPage
        {
            get => _currentPage;
            private set
            {
                if (SetField(ref _currentPage, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public int TotalPages
        {
            get => _totalPages;
            private set
            {
                if (SetField(ref _totalPages, Math.Max(1, value)))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public int TotalItems
        {
            get => _totalItems;
            private set => SetField(ref _totalItems, value);
        }

        public ICommand SearchCommand { get; }

        public ICommand AddUserCommand { get; }

        public ICommand DeleteUserCommand { get; }

        public ICommand PreviousPageCommand { get; }

        public ICommand NextPageCommand { get; }

        private bool CanDeleteUser()
        {
            return SelectedUser != null && !string.Equals(SelectedUser.UserId, "admin", StringComparison.OrdinalIgnoreCase);
        }

        private Task LoadFirstPageAsync()
        {
            CurrentPage = 1;
            return LoadPageAsync(CurrentPage);
        }

        private Task LoadPreviousPageAsync()
        {
            if (CurrentPage <= 1)
            {
                return Task.CompletedTask;
            }

            return LoadPageAsync(CurrentPage - 1);
        }

        private Task LoadNextPageAsync()
        {
            if (CurrentPage >= TotalPages)
            {
                return Task.CompletedTask;
            }

            return LoadPageAsync(CurrentPage + 1);
        }

        private async Task LoadPageAsync(int page)
        {
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();

                var queryText = (SearchText ?? string.Empty).Trim();

                IReadOnlyList<UserProfile> pageItems;

                if (string.IsNullOrWhiteSpace(queryText))
                {
                    var baseQuery = context.UserProfiles.AsNoTracking();

                    TotalItems = await baseQuery.CountAsync();
                    TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);

                    pageItems = await baseQuery
                        .OrderBy(x => x.UserName)
                        .Skip((page - 1) * PageSize)
                        .Take(PageSize)
                        .ToListAsync();
                }
                else
                {
                    var snapshot = await context.UserProfiles.AsNoTracking().ToListAsync();
                    var ranked = snapshot
                        .Select(u => new { User = u, Score = CalculateUserNameScore(u.UserName, queryText) })
                        .Where(x => x.Score > 0)
                        .OrderByDescending(x => x.Score)
                        .ThenBy(x => x.User.UserName)
                        .Select(x => x.User)
                        .ToList();

                    TotalItems = ranked.Count;
                    TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);

                    pageItems = ranked
                        .Skip((page - 1) * PageSize)
                        .Take(PageSize)
                        .ToList();
                }

                CurrentPage = Math.Max(1, Math.Min(page, TotalPages));

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _users.Clear();
                    foreach (var item in pageItems)
                    {
                        _users.Add(item);
                    }

                    if (_users.Count == 0)
                    {
                        SelectedUser = null;
                    }
                    else if (SelectedUser == null || !_users.Any(x => x.UserId == SelectedUser.UserId))
                    {
                        SelectedUser = _users[0];
                    }

                    StatusText = string.IsNullOrWhiteSpace(queryText)
                        ? $"共 {TotalItems} 个用户"
                        : $"搜索到 {TotalItems} 个用户";
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    StatusText = $"加载用户失败：{ex.Message}";
                });
            }
        }

        private async Task AddUserAsync()
        {
            var vm = new AddUserDialogViewModel();
            var dialog = new Upcomputer.UI.Views.AddUserWindow(vm)
            {
                Owner = Application.Current?.MainWindow
            };

            var result = dialog.ShowDialog();
            if (result != true)
            {
                return;
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();

                var user = new UserProfile
                {
                    UserId = Guid.NewGuid().ToString("N"),
                    UserName = vm.UserName.Trim(),
                    Gender = vm.Gender,
                    BirthDate = vm.BirthDate,
                    PhoneNumber = string.IsNullOrWhiteSpace(vm.PhoneNumber) ? null : vm.PhoneNumber.Trim(),
                    HeightCm = vm.HeightCm,
                    WeightKg = vm.WeightKg,
                    CreatedAt = DateTime.Now
                };

                context.UserProfiles.Add(user);
                await context.SaveChangesAsync();

                StatusText = $"已新增用户：{user.UserName}";
                await LoadFirstPageAsync();

                SelectedUser = _users.FirstOrDefault(x => x.UserId == user.UserId) ?? SelectedUser;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"新增用户失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteSelectedUserAsync()
        {
            if (SelectedUser == null)
            {
                return;
            }

            if (string.Equals(SelectedUser.UserId, "admin", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("admin 用户不可删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var user = SelectedUser;
            var confirm = MessageBox.Show(
                $"确定删除用户 \"{user.UserName}\" 吗？\n（删除后不可恢复）",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync();

                var entity = await context.UserProfiles.FirstOrDefaultAsync(x => x.UserId == user.UserId);
                if (entity == null)
                {
                    StatusText = "用户已不存在。";
                    await LoadPageAsync(CurrentPage);
                    return;
                }

                context.UserProfiles.Remove(entity);
                await context.SaveChangesAsync();

                StatusText = $"已删除用户：{user.UserName}";

                // 重新加载当前页；如果当前页被删空则回退一页
                await LoadPageAsync(CurrentPage);
                if (_users.Count == 0 && CurrentPage > 1)
                {
                    await LoadPageAsync(CurrentPage - 1);
                }
            }
            catch (DbUpdateException ex)
            {
                MessageBox.Show($"删除失败：该用户可能有关联的治疗记录，无法删除。\n{ex.Message}", "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除用户失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static int CalculateUserNameScore(string? userName, string query)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(query))
            {
                return 0;
            }

            var name = userName.Trim();
            var q = query.Trim();

            var nameLower = name.ToLowerInvariant();
            var qLower = q.ToLowerInvariant();

            if (nameLower == qLower) return 1000;

            if (nameLower.StartsWith(qLower))
            {
                return 900 - Math.Min(100, Math.Max(0, name.Length - q.Length));
            }

            var idx = nameLower.IndexOf(qLower, StringComparison.Ordinal);
            if (idx >= 0)
            {
                return 700 - Math.Min(200, idx);
            }

            var score = 0;
            var lastPos = -1;
            foreach (var ch in qLower)
            {
                var pos = nameLower.IndexOf(ch, lastPos + 1);
                if (pos < 0) return 0;

                var gap = pos - lastPos - 1;
                score += Math.Max(1, 15 - gap);
                lastPos = pos;
            }

            return 300 + Math.Min(200, score);
        }
    }

    public class AddUserDialogViewModel : ObservableObject
    {
        private string _userName = string.Empty;
        private string? _gender = "未知";
        private DateTime? _birthDate;
        private string? _phoneNumber;
        private string _heightText = string.Empty;
        private string _weightText = string.Empty;

        public string UserName
        {
            get => _userName;
            set => SetField(ref _userName, value ?? string.Empty);
        }

        public string? Gender
        {
            get => _gender;
            set => SetField(ref _gender, value);
        }

        public DateTime? BirthDate
        {
            get => _birthDate;
            set => SetField(ref _birthDate, value);
        }

        public string? PhoneNumber
        {
            get => _phoneNumber;
            set => SetField(ref _phoneNumber, value);
        }

        public string HeightText
        {
            get => _heightText;
            set
            {
                if (SetField(ref _heightText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(BmiDisplay));
                }
            }
        }

        public string WeightText
        {
            get => _weightText;
            set
            {
                if (SetField(ref _weightText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(BmiDisplay));
                }
            }
        }

        public double? HeightCm => TryParseDouble(HeightText);

        public double? WeightKg => TryParseDouble(WeightText);

        public string BmiDisplay
        {
            get
            {
                var h = HeightCm;
                var w = WeightKg;
                if (h is null || w is null)
                {
                    return "-";
                }

                var hm = h.Value / 100.0;
                if (hm <= 0)
                {
                    return "-";
                }

                var bmi = w.Value / (hm * hm);
                return bmi.ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        public bool Validate(out string error)
        {
            var name = (UserName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "用户名不能为空。";
                return false;
            }

            if (name.Length > 100)
            {
                error = "用户名长度不能超过 100。";
                return false;
            }

            if (BirthDate is not null && BirthDate.Value.Date > DateTime.Now.Date)
            {
                error = "出生日期不能晚于今天。";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(PhoneNumber) && PhoneNumber!.Trim().Length > 30)
            {
                error = "手机号长度不能超过 30。";
                return false;
            }

            var h = HeightCm;
            if (h is not null && (h.Value < 50 || h.Value > 250))
            {
                error = "身高请输入 50~250 cm。";
                return false;
            }

            var w = WeightKg;
            if (w is not null && (w.Value < 2 || w.Value > 300))
            {
                error = "体重请输入 2~300 kg。";
                return false;
            }

            if ((HeightText ?? string.Empty).Trim().Length > 0 && h is null)
            {
                error = "身高请输入数字。";
                return false;
            }

            if ((WeightText ?? string.Empty).Trim().Length > 0 && w is null)
            {
                error = "体重请输入数字。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static double? TryParseDouble(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }

            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v))
            {
                return v;
            }

            return null;
        }
    }
}
