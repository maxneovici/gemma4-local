namespace LLLMax.Api.Documents;

public interface IDocumentService
{
    Task<DocumentUploadResponse> SaveUploadAsync(IFormFile file, CancellationToken cancellationToken);

    Task<DocumentVectorizeResponse> VectorizeFolderAsync(
        DocumentVectorizeRequest request,
        CancellationToken cancellationToken,
        Func<DocumentVectorizeProgress, CancellationToken, Task>? onProgress = null);

    Task<OcrResponse> ExtractTextAsync(OcrRequest request, CancellationToken cancellationToken);

    Task<InvoiceExtractionResponse> ExtractInvoiceAsync(InvoiceExtractionRequest request, CancellationToken cancellationToken);
}
