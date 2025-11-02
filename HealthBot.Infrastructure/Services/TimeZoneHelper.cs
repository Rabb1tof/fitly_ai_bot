using System;
using System.Globalization;

namespace HealthBot.Infrastructure.Services;

internal static class TimeZoneHelper
{
    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && TryResolve(timeZoneId, out var tz))
        {
            return tz;
        }

        return TimeZoneInfo.Utc;
    }

    public static bool TryResolve(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        if (timeZoneId.StartsWith("UTC", StringComparison.OrdinalIgnoreCase) &&
            timeZoneId.Length <= 7 &&
            TryParseOffset(timeZoneId.AsSpan(3), out var offset))
        {
            var id = timeZoneId.ToUpperInvariant();
            timeZone = TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    public static DateTime ConvertUtcToUserTime(DateTime utcDateTime, TimeZoneInfo timeZone)
    {
        var utc = utcDateTime.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc),
            DateTimeKind.Local => utcDateTime.ToUniversalTime(),
            _ => utcDateTime
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
    }

    private static bool TryParseOffset(ReadOnlySpan<char> span, out TimeSpan offset)
    {
        offset = default;

        if (span.IsEmpty)
        {
            offset = TimeSpan.Zero;
            return true;
        }

        var signChar = span[0];
        if (signChar != '+' && signChar != '-')
        {
            return false;
        }

        if (!int.TryParse(span[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours))
        {
            return false;
        }

        if (hours is < 0 or > 14)
        {
            return false;
        }

        offset = TimeSpan.FromHours(signChar == '-' ? -hours : hours);
        return true;
    }
}
