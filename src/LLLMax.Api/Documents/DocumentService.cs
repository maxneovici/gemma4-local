using LLLMax.Api.Memory;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Documents;

public sealed class DocumentService(
    LocalDataPaths paths,
    ILocalMemoryStore memoryStore,
    IOllamaApi ollamaApi,
    IOptions<LocalAiOptions> options) : IDocumentService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv", ".xml", ".html", ".css", ".js", ".ts", ".cs", ".sql", ".log"
    };

    private readonly LocalAiOptions _options = options.Value;

    public async Task<DocumentUploadResponse> SaveUploadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            throw new ArgumentException("Uploaded file is empty.", nameof(file));
        }

        if (file.Length > _options.Documents.MaxUploadBytes)
        {
            throw new ArgumentException($"Uploaded file exceeds {_options.Documents.MaxUploadBytes} bytes.", nameof(file));
        }

        var id = Guid.NewGuid().ToString("n");
        var safeName = Path.GetFileName(file.FileName);
        var storedName = $"{id}_{safeName}";
        var path = Path.Combine(paths.DocumentDirectory, storedName);

        await using var stream = File.Create(path);
        await file.CopyToAsync(stream, cancellationToken);

        return new DocumentUploadResponse(id, safeName, file.Length, path);
    }

    public async Task<DocumentVectorizeResponse> VectorizeFolderAsync(
        DocumentVectorizeRequest request,
        CancellationToken cancellationToken,
        Func<DocumentVectorizeProgress, CancellationToken, Task>? onProgress = null)
    {
        var folder = paths.Resolve(request.FolderPath);
        EnsureFolderAllowed(folder);

        if (!Directory.Exists(folder))
        {
            throw new ArgumentException($"Folder '{request.FolderPath}' does not exist.", nameof(request));
        }

        var files = Directory.EnumerateFiles(folder, request.SearchPattern, SearchOption.AllDirectories)
            .Where(file => TextExtensions.Contains(Path.GetExtension(file)))
            .OrderBy(file => Path.GetRelativePath(folder, file), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chunks = 0;

        var filesProcessed = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(file, cancellationToken);

            foreach (var chunk in Chunk(text, _options.Documents.ChunkSizeCharacters))
            {
                chunks++;
                var metadata = BuildDocumentMetadata(request, folder, file);

                await memoryStore.UpsertAsync(new MemoryUpsertRequest(
                    Collection: request.Collection,
                    Text: chunk,
                    Metadata: metadata), cancellationToken);
            }

            filesProcessed++;

            if (onProgress is not null)
            {
                await onProgress(new DocumentVectorizeProgress(
                    FilesProcessed: filesProcessed,
                    FileCount: files.Count,
                    ChunksWritten: chunks,
                    CurrentFile: Path.GetRelativePath(folder, file)), cancellationToken);
            }
        }

        return new DocumentVectorizeResponse(request.Collection, files.Count, chunks);
    }

    public async Task<OcrResponse> ExtractTextAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var (file, imageBase64) = await LoadImageAsync(request.DocumentId, cancellationToken);
        var model = ResolveVisionModel(request.Model);
        var prompt = request.Prompt ?? "Extract all visible text from this document. Preserve tables and line breaks where useful.";
        var response = await VisionChatAsync(model, prompt, imageBase64, cancellationToken);

        await memoryStore.UpsertAsync(new MemoryUpsertRequest(
            Collection: "documents",
            Text: response.Message?.Content ?? string.Empty,
            Metadata: new Dictionary<string, string>
            {
                ["source"] = file,
                ["kind"] = "ocr"
            }), cancellationToken);

        return new OcrResponse(request.DocumentId, response.Message?.Content ?? string.Empty, response.Model);
    }

    public async Task<InvoiceExtractionResponse> ExtractInvoiceAsync(InvoiceExtractionRequest request, CancellationToken cancellationToken)
    {
        var (file, imageBase64) = await LoadImageAsync(request.DocumentId, cancellationToken);
        var model = ResolveVisionModel(request.Model);
        var prompt = "Extract invoice data from this document as strict JSON with keys: supplierName, supplierVatId, invoiceNumber, invoiceDate, dueDate, currency, subtotal, taxTotal, total, lineItems[]. Use null for missing values and do not include markdown.";
        var response = await VisionChatAsync(model, prompt, imageBase64, cancellationToken);
        var json = response.Message?.Content ?? string.Empty;

        await memoryStore.UpsertAsync(new MemoryUpsertRequest(
            Collection: "invoices",
            Text: json,
            Metadata: new Dictionary<string, string>
            {
                ["source"] = file,
                ["kind"] = "invoice_extraction"
            }), cancellationToken);

        return new InvoiceExtractionResponse(request.DocumentId, json, response.Model);
    }

    private async Task<OllamaChatResponse> VisionChatAsync(string model, string prompt, string imageBase64, CancellationToken cancellationToken) =>
        await ollamaApi.ChatVisionAsync(new OllamaVisionChatRequest(
            Model: model,
            Stream: false,
            Messages:
            [
                new OllamaVisionMessage("user", prompt, [imageBase64])
            ],
            Options: new OllamaOptions(0.1, _options.Sampling.TopP, _options.Sampling.TopK)), cancellationToken);

    private async Task<(string File, string Base64)> LoadImageAsync(string documentId, CancellationToken cancellationToken)
    {
        var file = Directory.EnumerateFiles(paths.DocumentDirectory, $"{documentId}_*").SingleOrDefault()
            ?? throw new InvalidOperationException($"Document '{documentId}' does not exist.");

        var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
        return (file, Convert.ToBase64String(bytes));
    }

    private string ResolveVisionModel(string? model) =>
        model ?? _options.VisionModel ?? _options.DefaultModel;

    private static IReadOnlyDictionary<string, string> BuildDocumentMetadata(DocumentVectorizeRequest request, string folder, string file)
    {
        var relativePath = Path.GetRelativePath(folder, file);
        var relativeDirectory = Path.GetDirectoryName(relativePath);
        var metadata = new Dictionary<string, string>(request.Metadata ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = file,
            ["sourceFile"] = Path.GetFileName(file),
            ["sourceRelativePath"] = relativePath,
            ["sourceDirectory"] = string.IsNullOrWhiteSpace(relativeDirectory) ? "." : relativeDirectory,
            ["kind"] = "document_chunk"
        };

        if (!string.IsNullOrWhiteSpace(request.Tenant))
        {
            metadata["tenant"] = request.Tenant.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            metadata["category"] = request.Category.Trim();
        }

        return metadata;
    }

    private void EnsureFolderAllowed(string folder)
    {
        var allowedRoots = _options.Documents.AllowedFolderRoots
            .Select(paths.Resolve)
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToList();

        var normalized = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!allowedRoots.Any(root => normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Folder vectorization is restricted to configured LocalAi:Documents:AllowedFolderRoots.");
        }
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (var index = 0; index < text.Length; index += size)
        {
            yield return text.Substring(index, Math.Min(size, text.Length - index));
        }
    }
}
