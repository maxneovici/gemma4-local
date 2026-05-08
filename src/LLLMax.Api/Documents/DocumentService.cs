using System.Security.Cryptography;
using System.Text;
using LLLMax.Api.Memory;
using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using LLLMax.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Documents;

public sealed class DocumentService(
    LocalDataPaths paths,
    ILocalMemoryStore memoryStore,
    IOllamaApi ollamaApi,
    IDbContextFactory<LocalDbContext> dbFactory,
    ILogger<DocumentService> logger,
    IOptions<LocalAiOptions> options) : IDocumentService
{
    private const string ImportMarker = "documents_file_metadata_imported";
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv", ".xml", ".html", ".css", ".js", ".ts", ".cs", ".sql", ".log"
    };

    private readonly LocalAiOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _imported;

    public async Task<DocumentUploadResponse> SaveUploadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await EnsureImportedAsync(cancellationToken);

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

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.Documents.Add(new DocumentEntity
            {
                Id = id,
                FileName = safeName,
                StoredFileName = storedName,
                Path = path,
                Bytes = file.Length,
                ContentType = file.ContentType,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

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
        var skippedFiles = 0;
        var filesProcessed = 0;
        var collection = string.IsNullOrWhiteSpace(request.Collection) ? MemoryLayers.Knowledge : request.Collection.Trim();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var contentHash = ContentHash(text);
            var relativePath = Path.GetRelativePath(folder, file);

            if (await IsAlreadyVectorizedAsync(collection, contentHash, cancellationToken))
            {
                skippedFiles++;
                filesProcessed++;

                if (onProgress is not null)
                {
                    await onProgress(new DocumentVectorizeProgress(
                        FilesProcessed: filesProcessed,
                        FileCount: files.Count,
                        ChunksWritten: chunks,
                        CurrentFile: relativePath,
                        SkippedFileCount: skippedFiles), cancellationToken);
                }

                continue;
            }

            var items = new List<MemoryUpsertItem>();
            var chunkIndex = 0;
            var fileChunks = Chunk(text, _options.Documents.ChunkSizeCharacters).ToList();

            foreach (var chunk in fileChunks)
            {
                var metadata = BuildDocumentMetadata(request, folder, file, contentHash, chunkIndex, fileChunks.Count);
                items.Add(new MemoryUpsertItem(chunk, metadata));
                chunkIndex++;
            }

            if (items.Count > 0)
            {
                var response = await memoryStore.UpsertBatchAsync(new MemoryBatchUpsertRequest(collection, items), cancellationToken);
                chunks += response.Ids.Count;
            }

            filesProcessed++;

            if (onProgress is not null)
            {
                await onProgress(new DocumentVectorizeProgress(
                    FilesProcessed: filesProcessed,
                    FileCount: files.Count,
                    ChunksWritten: chunks,
                    CurrentFile: relativePath,
                    SkippedFileCount: skippedFiles), cancellationToken);
            }
        }

        return new DocumentVectorizeResponse(collection, files.Count, chunks, skippedFiles);
    }

    public async Task<OcrResponse> ExtractTextAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var (file, imageBase64) = await LoadImageAsync(request.DocumentId, cancellationToken);
        var model = ResolveVisionModel(request.Model);
        var prompt = request.Prompt ?? "Extract all visible text from this document. Preserve tables and line breaks where useful.";
        var response = await VisionChatAsync(model, prompt, imageBase64, cancellationToken);

        await memoryStore.UpsertAsync(new MemoryUpsertRequest(
            Collection: MemoryLayers.Knowledge,
            Text: response.Message?.Content ?? string.Empty,
            Metadata: MemoryMetadata.Build(new Dictionary<string, string>
            {
                ["source"] = file,
                ["kind"] = "ocr"
            }, MemoryLayers.Knowledge, response.Message?.Content ?? string.Empty, "ocr", "knowledge", confidence: 0.66, reviewRequired: true)), cancellationToken);

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
            Collection: MemoryLayers.Knowledge,
            Text: json,
            Metadata: MemoryMetadata.Build(new Dictionary<string, string>
            {
                ["source"] = file,
                ["kind"] = "invoice_extraction"
            }, MemoryLayers.Knowledge, json, "invoice_extraction", "knowledge", confidence: 0.66, reviewRequired: true)), cancellationToken);

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
        await EnsureImportedAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(document => document.Id == documentId, cancellationToken);
        var file = document?.Path ?? Directory.EnumerateFiles(paths.DocumentDirectory, $"{documentId}_*").SingleOrDefault()
            ?? throw new InvalidOperationException($"Document '{documentId}' does not exist.");

        var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
        return (file, Convert.ToBase64String(bytes));
    }

    private async Task EnsureImportedAsync(CancellationToken cancellationToken) =>
        await JsonImport.ImportOnceAsync(dbFactory, _gate, () => _imported, () => _imported = true, ImportMarker, ImportFileMetadataAsync, cancellationToken);

    private async Task ImportFileMetadataAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.DocumentDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(paths.DocumentDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var storedName = Path.GetFileName(file);
            var separatorIndex = storedName.IndexOf('_', StringComparison.Ordinal);

            if (separatorIndex <= 0)
            {
                continue;
            }

            var id = storedName[..separatorIndex];

            try
            {
                if (await db.Documents.AnyAsync(document => document.Id == id, cancellationToken))
                {
                    continue;
                }

                var info = new FileInfo(file);
                db.Documents.Add(new DocumentEntity
                {
                    Id = id,
                    FileName = storedName[(separatorIndex + 1)..],
                    StoredFileName = storedName,
                    Path = file,
                    Bytes = info.Length,
                    ContentType = null,
                    CreatedAt = info.CreationTimeUtc == DateTime.MinValue ? DateTimeOffset.UtcNow : new DateTimeOffset(info.CreationTimeUtc)
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Could not import document metadata for file {File}", file);
            }
        }
    }

    private string ResolveVisionModel(string? model) =>
        model ?? _options.VisionModel ?? _options.DefaultModel;

    private async Task<bool> IsAlreadyVectorizedAsync(string collection, string contentHash, CancellationToken cancellationToken)
    {
        var count = await memoryStore.CountAsync(new MemoryCountRequest(collection, new Dictionary<string, string>
        {
            ["contentHash"] = contentHash,
            ["kind"] = "document_chunk"
        }), cancellationToken);

        return count.Count > 0;
    }

    private static IReadOnlyDictionary<string, string> BuildDocumentMetadata(DocumentVectorizeRequest request, string folder, string file, string contentHash, int chunkIndex, int chunkCount)
    {
        var relativePath = Path.GetRelativePath(folder, file);
        var relativeDirectory = Path.GetDirectoryName(relativePath);
        var metadata = MemoryMetadata.Build(request.Metadata, MemoryLayers.Knowledge, Path.GetFileName(file), "document_vectorization", "knowledge", confidence: 0.86);
        metadata["source"] = file;
        metadata["sourceFile"] = Path.GetFileName(file);
        metadata["sourceRelativePath"] = relativePath;
        metadata["sourceDirectory"] = string.IsNullOrWhiteSpace(relativeDirectory) ? "." : relativeDirectory;
        metadata["contentHash"] = contentHash;
        metadata["chunkIndex"] = chunkIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["chunkCount"] = chunkCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        metadata["kind"] = "document_chunk";
        metadata["observedAt"] = DateTimeOffset.UtcNow.ToString("O");

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

    private static string ContentHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
