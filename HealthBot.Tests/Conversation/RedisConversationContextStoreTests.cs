using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HealthBot.Core.Entities;
using HealthBot.Infrastructure.Data;
using HealthBot.Infrastructure.Telegram;
using HealthBot.Shared.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HealthBot.Tests.Conversation;

public sealed class RedisConversationContextStoreTests : IDisposable
{
    private readonly Mock<IRedisCacheService> _cacheMock = new();
    private readonly Mock<IConversationSessionRepository> _repositoryMock = new();
    private readonly ServiceProvider _provider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RedisOptions _options = new() { ConversationSessionTtlMinutes = 60 };

    public RedisConversationContextStoreTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repositoryMock.Object);
        _provider = services.BuildServiceProvider();
        _scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsCachedContext_WhenCacheHit()
    {
        var chatId = 42L;
        var cached = new ConversationContext { Flow = ConversationFlow.Custom };
        _cacheMock.Setup(c => c.GetAsync<ConversationContext>("session:42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var store = CreateStore();
        var result = await store.GetSessionAsync(chatId);

        result.Should().BeSameAs(cached);
        _repositoryMock.Verify(r => r.GetAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSessionAsync_RestoresFromRepository_WhenCacheMiss()
    {
        var chatId = 99L;
        _cacheMock.Setup(c => c.GetAsync<ConversationContext>("session:99", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationContext?)null);

        var session = new ConversationSession
        {
            ChatId = chatId,
            Flow = (int)ConversationFlow.Template,
            Stage = (int)ConversationStage.AwaitingRepeatMinutes,
            TemplateCode = "code",
            TemplateId = Guid.NewGuid(),
            TemplateTitle = "title",
            TemplateDefaultRepeat = 15,
            CustomMessage = "hello",
            FirstDelayMinutes = 5,
            ExpectManualInput = true,
            LastBotMessageId = 123
        };

        _repositoryMock.Setup(r => r.GetAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _cacheMock.Setup(c => c.SetAsync(
                "session:99",
                It.IsAny<ConversationContext>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var store = CreateStore();
        var result = await store.GetSessionAsync(chatId);

        result.Flow.Should().Be(ConversationFlow.Template);
        result.Stage.Should().Be(ConversationStage.AwaitingRepeatMinutes);
        result.TemplateCode.Should().Be(session.TemplateCode);
        result.TemplateId.Should().Be(session.TemplateId);
        result.TemplateTitle.Should().Be(session.TemplateTitle);
        result.TemplateDefaultRepeat.Should().Be(session.TemplateDefaultRepeat);
        result.CustomMessage.Should().Be(session.CustomMessage);
        result.FirstDelayMinutes.Should().Be(session.FirstDelayMinutes);
        result.ExpectManualInput.Should().BeTrue();
        result.LastBotMessageId.Should().Be(session.LastBotMessageId);

        _cacheMock.Verify(c => c.SetAsync(
            "session:99",
            It.IsAny<ConversationContext>(),
            It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(_options.ConversationSessionTtlMinutes)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsNewContext_WhenRepositoryEmpty()
    {
        var chatId = 77L;
        _cacheMock.Setup(c => c.GetAsync<ConversationContext>("session:77", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationContext?)null);
        _repositoryMock.Setup(r => r.GetAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationSession?)null);

        var store = CreateStore();
        var result = await store.GetSessionAsync(chatId);

        result.Should().NotBeNull();
        result.Flow.Should().Be(ConversationFlow.None);
        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<ConversationContext>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveSessionAsync_PersistsAndCaches()
    {
        var chatId = 55L;
        var context = new ConversationContext
        {
            Flow = ConversationFlow.Custom,
            Stage = ConversationStage.AwaitingFirstDelayMinutes,
            CustomMessage = "custom",
            FirstDelayMinutes = 42,
            ExpectManualInput = true,
            LastBotMessageId = 321
        };

        ConversationSession? captured = null;
        _repositoryMock.Setup(r => r.UpsertAsync(It.IsAny<ConversationSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<ConversationSession, CancellationToken>((session, _) => captured = session);

        _cacheMock.Setup(c => c.SetAsync(
                "session:55",
                It.IsAny<ConversationContext>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var store = CreateStore();
        await store.SaveSessionAsync(chatId, context);

        captured.Should().NotBeNull();
        captured!.ChatId.Should().Be(chatId);
        captured.Flow.Should().Be((int)context.Flow);
        captured.Stage.Should().Be((int)context.Stage);
        captured.CustomMessage.Should().Be(context.CustomMessage);
        captured.FirstDelayMinutes.Should().Be(context.FirstDelayMinutes);
        captured.ExpectManualInput.Should().BeTrue();
        captured.LastBotMessageId.Should().Be(context.LastBotMessageId);

        _cacheMock.Verify(c => c.SetAsync(
            "session:55",
            context,
            It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromMinutes(_options.ConversationSessionTtlMinutes)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteSessionAsync_RemovesFromRepositoryAndCache()
    {
        var chatId = 12L;
        _cacheMock.Setup(c => c.RemoveAsync("session:12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repositoryMock.Setup(r => r.DeleteAsync(chatId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var store = CreateStore();
        await store.DeleteSessionAsync(chatId);

        _repositoryMock.Verify(r => r.DeleteAsync(chatId, It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.RemoveAsync("session:12", It.IsAny<CancellationToken>()), Times.Once);
    }

    private RedisConversationContextStore CreateStore()
    {
        _repositoryMock.Invocations.Clear();
        _cacheMock.Invocations.Clear();
        return new RedisConversationContextStore(_cacheMock.Object, _scopeFactory, Options.Create(_options));
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}
