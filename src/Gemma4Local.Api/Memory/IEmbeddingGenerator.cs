namespace Gemma4Local.Api.Memory;

public interface IEmbeddingGenerator
{
    Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken);
}
