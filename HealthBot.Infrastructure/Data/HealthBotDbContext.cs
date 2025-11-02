using System;
using HealthBot.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HealthBot.Infrastructure.Data;

public class HealthBotDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<ReminderTemplate> ReminderTemplates => Set<ReminderTemplate>();

    public HealthBotDbContext(DbContextOptions<HealthBotDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.TelegramId).IsUnique();
            entity.Property(u => u.TelegramId).IsRequired();
            entity.Property(u => u.Username).HasMaxLength(64);
            entity.Property(u => u.CreatedAt).HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<Reminder>(entity =>
        {
            entity.ToTable("reminders");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Message).IsRequired();
            entity.Property(r => r.ScheduledAt).IsRequired();
            entity.Property(r => r.NextTriggerAt).IsRequired();
            entity.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
            entity.Property(r => r.IsActive).HasDefaultValue(true);
            entity.Property(r => r.RepeatIntervalMinutes);
            entity.Property(r => r.LastTriggeredAt);

            entity.HasOne(r => r.User)
                .WithMany(u => u.Reminders)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Template)
                .WithMany(t => t.Reminders)
                .HasForeignKey(r => r.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(r => new { r.UserId, r.IsActive });
            entity.HasIndex(r => r.NextTriggerAt);
        });

        modelBuilder.Entity<ReminderTemplate>(entity =>
        {
            entity.ToTable("reminder_templates");
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.Code).IsUnique();
            entity.Property(t => t.Code).IsRequired().HasMaxLength(64);
            entity.Property(t => t.Title).IsRequired().HasMaxLength(128);
            entity.Property(t => t.Description).HasMaxLength(512);

            entity.HasData(
                new ReminderTemplate
                {
                    Id = Guid.Parse("d3c63a05-3cb3-4dd0-a6a5-8f3483c962dd"),
                    Code = "stretch",
                    Title = "Размяться, хватит сидеть",
                    Description = "Короткая разминка, чтобы размять мышцы.",
                    DefaultRepeatIntervalMinutes = 60,
                    IsSystem = true
                },
                new ReminderTemplate
                {
                    Id = Guid.Parse("a6df40eb-8138-4949-9305-1c9458c7bf57"),
                    Code = "drink",
                    Title = "Попей воды!",
                    Description = "Напоминание сделать паузу и выпить воды.",
                    DefaultRepeatIntervalMinutes = 45,
                    IsSystem = true
                },
                new ReminderTemplate
                {
                    Id = Guid.Parse("4a9df57a-3958-4e32-9b6d-3f5e6f5729fd"),
                    Code = "eat",
                    Title = "Покушай",
                    Description = "Своевременный перекус или обед.",
                    DefaultRepeatIntervalMinutes = 180,
                    IsSystem = true
                });
        });
    }
}
