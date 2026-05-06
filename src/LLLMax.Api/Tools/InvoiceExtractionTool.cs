using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LLLMax.Api.Documents;

namespace LLLMax.Api.Tools;

public sealed class InvoiceExtractionTool(IDocumentService documentService) : LocalToolBase<InvoiceExtractionArguments>
{
    public override string Name => "extract_invoice";

    public override string Description => "Extract structured invoice fields from an uploaded document image with a local vision model.";

    protected override async Task<LocalToolResult> InvokeAsync(InvoiceExtractionArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var response = await documentService.ExtractInvoiceAsync(new InvoiceExtractionRequest(
            DocumentId: arguments.DocumentId,
            Model: arguments.Model), cancellationToken);

        return new LocalToolResult(JsonSerializer.Serialize(response));
    }
}

public sealed record InvoiceExtractionArguments([property: Required] string DocumentId, string? Model = null);
