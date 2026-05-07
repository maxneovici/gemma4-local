namespace LLLMax.Api.Documents;

public sealed record DocumentUploadResponse(string Id, string FileName, long Bytes, string Path);

public sealed record DocumentVectorizeRequest(
    string FolderPath,
    string Collection = "documents",
    string SearchPattern = "*.*",
    string? Tenant = null,
    string? Category = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record DocumentVectorizeResponse(string Collection, int FileCount, int ChunkCount);

public sealed record DocumentVectorizeProgress(int FilesProcessed, int FileCount, int ChunksWritten, string CurrentFile);

public sealed record OcrRequest(string DocumentId, string? Prompt = null, string? Model = null);

public sealed record OcrResponse(string DocumentId, string Text, string Model);

public sealed record InvoiceExtractionRequest(string DocumentId, string? Model = null);

public sealed record InvoiceExtractionResponse(string DocumentId, string Json, string Model);
