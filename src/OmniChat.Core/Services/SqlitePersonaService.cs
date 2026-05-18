using Microsoft.EntityFrameworkCore;
using OmniChat.Core.Data;
using OmniChat.Core.Data.Entities;

namespace OmniChat.Core.Services;

public sealed class SqlitePersonaService : IPersonaService
{
    private readonly IDbContextFactory<OmniChatDbContext> _factory;

    public SqlitePersonaService(IDbContextFactory<OmniChatDbContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<IReadOnlyList<Persona>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Personas
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new Persona(p.Id, p.Name, p.SystemPrompt, p.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Persona> UpsertAsync(Persona persona, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persona);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        var id = string.IsNullOrWhiteSpace(persona.Id) ? Guid.NewGuid().ToString("N") : persona.Id;
        var existing = await db.Personas.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (existing is null)
        {
            db.Personas.Add(new PersonaEntity
            {
                Id = id,
                Name = persona.Name.Trim(),
                SystemPrompt = persona.SystemPrompt,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Name = persona.Name.Trim();
            existing.SystemPrompt = persona.SystemPrompt;
        }
        await db.SaveChangesAsync(cancellationToken);

        var saved = await db.Personas.AsNoTracking().FirstAsync(p => p.Id == id, cancellationToken);
        return new Persona(saved.Id, saved.Name, saved.SystemPrompt, saved.CreatedAt);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Personas.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
        return rows > 0;
    }
}
