using System.Collections.Concurrent;

namespace OmniChat.Core.Services;

public sealed class InMemoryPersonaService : IPersonaService
{
    private readonly ConcurrentDictionary<string, Persona> _personas = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<Persona>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Persona> list = _personas.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(list);
    }

    public Task<Persona> UpsertAsync(Persona persona, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persona);
        var id = string.IsNullOrWhiteSpace(persona.Id) ? Guid.NewGuid().ToString("N") : persona.Id;
        var saved = persona with { Id = id, CreatedAt = persona.CreatedAt ?? DateTimeOffset.UtcNow };
        _personas[id] = saved;
        return Task.FromResult(saved);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_personas.TryRemove(id, out _));
    }
}
