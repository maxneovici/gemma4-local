using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LLLMax.Api.Documents;

namespace LLLMax.Api.Tools;

public sealed class DocumentVectorizeTool(IDocumentService documentService) : LocalToolBase<DocumentVectorizeArguments>
{
    public override string Name => "document_vectorize_folder";

    public override string Description => "Vectorize all supported text documents in an approved local folder into persistent local memory.";

    protected override async Task<LocalToolResult> InvokeAsync(DocumentVectorizeArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var response = await documentService.VectorizeFolderAsync(new DocumentVectorizeRequest(
            FolderPath: arguments.FolderPath,
            Collection: arguments.Collection ?? "documents",
            SearchPattern: arguments.SearchPattern ?? "*.*",
            Tenant: arguments.Tenant,
            Category: arguments.Category,
            Metadata: arguments.Metadata), cancellationToken);

        return new LocalToolResult(JsonSerializer.Serialize(response));
    }
}

public sealed record DocumentVectorizeArguments(
    [property: Required] string FolderPath,
    string? Collection = null,
    string? SearchPattern = null,
    string? Tenant = null,
    string? Category = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
