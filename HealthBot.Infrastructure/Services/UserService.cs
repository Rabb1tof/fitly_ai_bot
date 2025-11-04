using System;
using System.Threading;
using System.Threading.Tasks;
using HealthBot.Core.Entities;
using HealthBot.Infrastructure.Data;
using HealthBot.Shared.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HealthBot.Infrastructure.Services;

public class UserService
{
    private readonly HealthBotDbContext _dbContext;
    private readonly IRedisCacheService _cache;
    private readonly RedisOptions _redisOptions;

    public UserService(
        HealthBotDbContext dbContext,
        IRedisCacheService cache,
        IOptions<RedisOptions> redisOptions)
    {
        _dbContext = dbContext;
        _cache = cache;
        _redisOptions = redisOptions.Value;
    }

    public async Task<User> RegisterUserAsync(long telegramId, string? username, CancellationToken cancellationToken = default)
    {
        var cacheKey = RedisCacheKeys.UserProfile(telegramId);
        var cachedUser = await _cache.GetAsync<User>(cacheKey, cancellationToken);

        if (cachedUser is not null)
        {
            if (username is not null && !string.Equals(cachedUser.Username, username, StringComparison.Ordinal))
            {
                var userToUpdate = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.TelegramId == telegramId, cancellationToken);
                
                if (userToUpdate is not null)
                {
                    userToUpdate.Username = username;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    _dbContext.Entry(userToUpdate).State = EntityState.Detached;
                    cachedUser.Username = username;
                    await CacheUserAsync(cachedUser, cancellationToken);
                }
            }

            return cachedUser;
        }

        var existingUser = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramId == telegramId, cancellationToken);

        if (existingUser is not null)
        {
            if (username is not null && !string.Equals(existingUser.Username, username, StringComparison.Ordinal))
            {
                var userToUpdate = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.TelegramId == telegramId, cancellationToken);
                
                if (userToUpdate is not null)
                {
                    userToUpdate.Username = username;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    _dbContext.Entry(userToUpdate).State = EntityState.Detached;
                    existingUser.Username = username;
                }
            }

            await CacheUserAsync(existingUser, cancellationToken);
            return existingUser;
        }

        var user = new User
        {
            TelegramId = telegramId,
            Username = username
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(user).State = EntityState.Detached;
        await CacheUserAsync(user, cancellationToken);

        return user;
    }

    public async Task SetUserTimeZoneAsync(User user, string timeZoneId, CancellationToken cancellationToken = default)
    {
        var userToUpdate = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == user.Id, cancellationToken);
        
        if (userToUpdate is null)
        {
            return;
        }

        if (userToUpdate.TimeZoneId != timeZoneId)
        {
            userToUpdate.TimeZoneId = timeZoneId;
            await _dbContext.SaveChangesAsync(cancellationToken);
            user.TimeZoneId = timeZoneId;
        }

        _dbContext.Entry(userToUpdate).State = EntityState.Detached;
        await CacheUserAsync(user, cancellationToken);
    }

    private Task CacheUserAsync(User user, CancellationToken cancellationToken)
    {
        var cacheKey = RedisCacheKeys.UserProfile(user.TelegramId);
        return _cache.SetAsync(cacheKey, user, _redisOptions.GetDefaultTtl(), cancellationToken);
    }
}
