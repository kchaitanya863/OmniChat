using Microsoft.EntityFrameworkCore;
using OmniChat.Core.Data;
using OmniChat.Core.Data.Entities;
using OmniChat.Core.Models;

namespace OmniChat.Core.Services;

public sealed class SqliteProviderConfigStore : IProviderConfigStore
{
    private readonly IDbContextFactory<OmniChatDbContext> _factory;

    public SqliteProviderConfigStore(IDbContextFactory<OmniChatDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<IReadOnlyList<ProviderConfig>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ProviderConfigs.AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .ThenBy(p => p.DisplayName)
            .ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToArray();
    }

    public async Task<ProviderConfig?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ProviderConfigs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<ProviderConfig> UpsertAsync(ProviderConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.ProviderConfigs.FirstOrDefaultAsync(p => p.Id == config.Id, cancellationToken);

        if (existing is null)
        {
            db.ProviderConfigs.Add(new ProviderConfigEntity
            {
                Id = config.Id,
                DisplayName = config.DisplayName,
                ProviderType = config.ProviderType,
                Model = config.Model,
                IsEnabled = config.IsEnabled,
                ApiKeyCipher = config.ApiKeyCipher,
                KeyFingerprint = config.KeyFingerprint,
                BaseUrl = config.BaseUrl,
                SystemPrompt = config.SystemPrompt,
                Temperature = config.Temperature,
                MaxOutputTokens = config.MaxOutputTokens,
                CreatedAt = config.CreatedAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.DisplayName = config.DisplayName;
            existing.ProviderType = config.ProviderType;
            existing.Model = config.Model;
            existing.IsEnabled = config.IsEnabled;
            existing.ApiKeyCipher = config.ApiKeyCipher ?? existing.ApiKeyCipher;
            existing.KeyFingerprint = config.KeyFingerprint ?? existing.KeyFingerprint;
            existing.BaseUrl = config.BaseUrl ?? existing.BaseUrl;
            existing.SystemPrompt = config.SystemPrompt ?? existing.SystemPrompt;
            existing.Temperature = config.Temperature ?? existing.Temperature;
            existing.MaxOutputTokens = config.MaxOutputTokens ?? existing.MaxOutputTokens;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        var saved = await db.ProviderConfigs.AsNoTracking().FirstAsync(p => p.Id == config.Id, cancellationToken);
        return ToModel(saved);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ProviderConfigs.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
        return rows > 0;
    }

    private static ProviderConfig ToModel(ProviderConfigEntity e) => new(
        Id: e.Id,
        DisplayName: e.DisplayName,
        ProviderType: e.ProviderType,
        Model: e.Model,
        IsEnabled: e.IsEnabled,
        ApiKeyCipher: e.ApiKeyCipher,
        KeyFingerprint: e.KeyFingerprint,
        BaseUrl: e.BaseUrl,
        SystemPrompt: e.SystemPrompt,
        Temperature: e.Temperature,
        MaxOutputTokens: e.MaxOutputTokens,
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt);
}
