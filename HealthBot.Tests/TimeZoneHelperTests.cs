using System;
using FluentAssertions;
using HealthBot.Infrastructure.Services;
using Xunit;

namespace HealthBot.Tests;

public class TimeZoneHelperTests
{
    [Fact]
    public void Resolve_WhenIdIsNull_ReturnsUtc()
    {
        var tz = TimeZoneHelper.Resolve(null);

        tz.Id.Should().Be(TimeZoneInfo.Utc.Id);
    }

    [Fact]
    public void Resolve_WhenIdExists_ReturnsMatchingTimeZone()
    {
        var expected = TimeZoneInfo.Local.Id;

        var tz = TimeZoneHelper.Resolve(expected);

        tz.Id.Should().Be(expected);
    }

    [Theory]
    [InlineData("UTC+3", 3)]
    [InlineData("UTC-5", -5)]
    [InlineData("UTC", 0)]
    public void TryResolve_UtcOffsetFormats(string input, int expectedHoursOffset)
    {
        var result = TimeZoneHelper.TryResolve(input, out var tz);

        result.Should().BeTrue();
        tz.BaseUtcOffset.Should().Be(TimeSpan.FromHours(expectedHoursOffset));
    }

    [Fact]
    public void TryResolve_InvalidId_ReturnsFalse()
    {
        var result = TimeZoneHelper.TryResolve("Invalid/Zone", out var tz);

        result.Should().BeFalse();
        tz.Should().Be(TimeZoneInfo.Utc);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ConvertUtcToUserTime_SupportsDifferentKinds(DateTimeKind kind)
    {
        var utc = new DateTime(2025, 11, 2, 12, 0, 0, kind);
        if (utc.Kind == DateTimeKind.Local)
        {
            utc = utc.ToUniversalTime();
        }

        var tz = TimeZoneHelper.Resolve("UTC+3");

        var local = TimeZoneHelper.ConvertUtcToUserTime(utc, tz);

        local.Should().Be(new DateTime(2025, 11, 2, 15, 0, 0));
    }
}
