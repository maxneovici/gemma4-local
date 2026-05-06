using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LLLMax.Api.Documents;

namespace LLLMax.Api.Tools;

public sealed class OcrTool(IDocumentService documentService) : LocalToolBase<OcrArguments>
{
    public override string Name => "ocr_document";

    public override string Description => "Run local vision-model OCR against an uploaded document image.";

    protected override async Task<LocalToolResult> InvokeAsync(OcrArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        var response = await documentService.ExtractTextAsync(new OcrRequest(
            DocumentId: arguments.DocumentId,
            Prompt: arguments.Prompt,
            Model: arguments.Model), cancellationToken);

        return new LocalToolResult(JsonSerializer.Serialize(response));
    }
}

public sealed record OcrArguments([property: Required] string DocumentId, string? Prompt = null, string? Model = null);
