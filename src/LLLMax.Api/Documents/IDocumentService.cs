namespace LLLMax.Api.Documents;

public interface IDocumentService
{
    Task<DocumentUploadResponse> SaveUploadAsync(IFormFile file, CancellationToken cancellationToken);

    Task<DocumentVectorizeResponse> VectorizeFolderAsync(DocumentVectorizeRequest request, CancellationToken cancellationToken);

    Task<OcrResponse> ExtractTextAsync(OcrRequest request, CancellationToken cancellationToken);

    Task<InvoiceExtractionResponse> ExtractInvoiceAsync(InvoiceExtractionRequest request, CancellationToken cancellationToken);
}
