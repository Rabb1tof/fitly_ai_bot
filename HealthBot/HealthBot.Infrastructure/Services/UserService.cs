using System;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Core.Entities;
using HealthBot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthBot.Infrastructure.Services;

public class UserService
{
    private readonly HealthBotDbContext _dbContext;

    public UserService(HealthBotDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User> RegisterUserAsync(long telegramId, string? username, CancellationToken cancellationToken = default)
    {
        var existingUser = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId, cancellationToken);

        if (existingUser is not null)
        {
            if (!string.Equals(existingUser.Username, username, StringComparison.Ordinal) && username is not null)
            {
                existingUser.Username = username;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return existingUser;
        }

        var user = new User
        {
            TelegramId = telegramId,
            Username = username
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return user;
    }
}
