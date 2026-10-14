using Microsoft.EntityFrameworkCore;
using Upcomputer.Core.Models;

namespace Upcomputer.Data.Database
{
    /// <summary>
    /// 应用程序数据库上下文
    /// <para>
    /// 基于 Entity Framework Core + SQLite 实现数据持久化。
    /// 包含治疗会话、用户档案、压力日志、水肿日志、命令日志、医生建议和提醒等实体映射。
    /// 使用 <see cref="IDbContextFactory{TContext}"/> 模式创建实例，支持多线程并发访问。
    /// </para>
    /// </summary>
    public class AppDbContext : DbContext
    {
        /// <summary>
        /// 创建数据库上下文实例
        /// </summary>
        /// <param name="options">数据库上下文配置选项</param>
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        /// <summary>治疗会话表</summary>
        public DbSet<TreatmentSession> TreatmentSessions => Set<TreatmentSession>();

        /// <summary>用户（患者）档案表</summary>
        public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

        /// <summary>压力传感器数据日志表</summary>
        public DbSet<PressureData> PressureDataLogs => Set<PressureData>();

        /// <summary>水肿/阻抗数据日志表</summary>
        public DbSet<EdemaData> EdemaDataLogs => Set<EdemaData>();

        /// <summary>命令操作日志表</summary>
        public DbSet<CommandLog> CommandLogs => Set<CommandLog>();

        /// <summary>医生建议记录表</summary>
        public DbSet<DoctorAdviceRecord> DoctorAdviceRecords => Set<DoctorAdviceRecord>();

        /// <summary>治疗提醒表</summary>
        public DbSet<TreatmentReminder> TreatmentReminders => Set<TreatmentReminder>();

        /// <summary>
        /// 配置数据库连接（未显式配置时使用默认 SQLite 文件路径）
        /// </summary>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                DatabaseSettings.EnsureDatabaseDirectory();
                optionsBuilder.UseSqlite($"Data Source={DatabaseSettings.DatabaseFilePath}");
            }
        }

        /// <summary>
        /// 配置实体模型映射（表名、主键、索引、外键关系、字段约束等）
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TreatmentSession>(entity =>
            {
                entity.ToTable("TreatmentSessions");
                entity.HasKey(e => e.SessionId);
                entity.Property(e => e.SessionId).ValueGeneratedNever();
                entity.Property(e => e.UserId).HasMaxLength(64).HasDefaultValue("admin");
                entity.Property(e => e.Mode).HasConversion<byte>();
                entity.Property(e => e.DoctorAdvice).HasMaxLength(2000);
                entity.HasIndex(e => e.StartTime);
                entity.HasIndex(e => new { e.IsRunning, e.StartTime });
                entity.HasOne(e => e.User)
                    .WithMany(e => e.TreatmentSessions)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasMany(e => e.PressureLogs)
                    .WithOne(e => e.Session)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasMany(e => e.EdemaLogs)
                    .WithOne(e => e.Session)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasMany(e => e.CommandLogs)
                    .WithOne(e => e.Session)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.ToTable("UserProfiles");
                entity.HasKey(e => e.UserId);
                entity.Property(e => e.UserId).HasMaxLength(64);
                entity.Property(e => e.UserName).HasMaxLength(100);
                entity.Property(e => e.Gender).HasMaxLength(10);
                entity.Property(e => e.PhoneNumber).HasMaxLength(30);
                entity.Property(e => e.HeightCm).HasPrecision(6, 2);
                entity.Property(e => e.WeightKg).HasPrecision(6, 2);
                entity.HasIndex(e => e.UserName);
            });

            modelBuilder.Entity<PressureData>(entity =>
            {
                entity.ToTable("PressureLogs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.LatencyMs).HasDefaultValue(0);
                entity.HasIndex(e => new { e.SessionId, e.Timestamp });
                entity.HasIndex(e => e.Timestamp);
            });

            modelBuilder.Entity<EdemaData>(entity =>
            {
                entity.ToTable("EdemaLogs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.LatencyMs).HasDefaultValue(0);
                entity.HasIndex(e => new { e.SessionId, e.Timestamp });
                entity.HasIndex(e => e.Timestamp);
            });

            modelBuilder.Entity<CommandLog>(entity =>
            {
                entity.ToTable("CommandLogs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.CommandName).HasMaxLength(200);
                entity.Property(e => e.CommandDetails).HasMaxLength(2000);
                entity.HasIndex(e => new { e.SessionId, e.Timestamp });
                entity.HasIndex(e => e.Timestamp);
            });

            modelBuilder.Entity<DoctorAdviceRecord>(entity =>
            {
                entity.ToTable("DoctorAdviceRecords");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Content).HasMaxLength(4000);
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.UpdatedAt);
            });

            modelBuilder.Entity<TreatmentReminder>(entity =>
            {
                entity.ToTable("TreatmentReminders");
                entity.HasKey(e => e.ReminderId);
                entity.Property(e => e.ReminderId).ValueGeneratedNever();
                entity.Property(e => e.Content).HasMaxLength(2000);
                entity.HasIndex(e => new { e.IsCompleted, e.DueAt });
                entity.HasIndex(e => e.DueAt);
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}