namespace OmniChat.Core.Services;

public interface IPersonaService
{
    Task<IReadOnlyList<Persona>> ListAsync(CancellationToken cancellationToken = default);

    Task<Persona> UpsertAsync(Persona persona, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed record Persona(string Id, string Name, string SystemPrompt, DateTimeOffset? CreatedAt = null);
