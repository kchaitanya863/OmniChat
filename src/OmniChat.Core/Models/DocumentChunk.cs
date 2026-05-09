namespace OmniChat.Core.Models;

public sealed record DocumentChunk(
    string DocumentId,
    int Sequence,
    string Content,
    IReadOnlyList<float> Embedding);
