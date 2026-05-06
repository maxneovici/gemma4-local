using LLLMax.Api.Documents;

namespace LLLMax.Api.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/documents");

        group.MapPost("/upload", async (IFormFile file, IDocumentService documents, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await documents.SaveUploadAsync(file, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { Error = exception.Message });
            }
        }).DisableAntiforgery();

        group.MapPost("/vectorize-folder", async (DocumentVectorizeRequest request, IDocumentService documents, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await documents.VectorizeFolderAsync(request, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { Error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        group.MapPost("/ocr", async (OcrRequest request, IDocumentService documents, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await documents.ExtractTextAsync(request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        group.MapPost("/extract-invoice", async (InvoiceExtractionRequest request, IDocumentService documents, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await documents.ExtractInvoiceAsync(request, cancellationToken));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        return app;
    }
}
