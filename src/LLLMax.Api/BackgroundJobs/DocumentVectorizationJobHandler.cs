using System.Text.Json;
using LLLMax.Api.Documents;

namespace LLLMax.Api.BackgroundJobs;

public sealed class DocumentVectorizationJobHandler(IDocumentService documents) : IBackgroundJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => BackgroundJobKinds.DocumentVectorizeFolder;

    public async Task<string> RunAsync(BackgroundJob job, IBackgroundJobContext context, CancellationToken cancellationToken)
    {
        var request = job.Payload.Deserialize<DocumentVectorizeRequest>(JsonOptions)
            ?? throw new ArgumentException("Document vectorization job payload could not be deserialized.");

        await context.ReportAsync(new BackgroundJobProgress(0, 0, $"Vectorizing {request.FolderPath}..."), cancellationToken);

        var response = await documents.VectorizeFolderAsync(request, cancellationToken, async (progress, token) =>
        {
            await context.ReportAsync(new BackgroundJobProgress(
                Current: progress.FilesProcessed,
                Total: progress.FileCount,
                Message: $"Vectorized {progress.FilesProcessed}/{progress.FileCount} files; {progress.ChunksWritten} chunks. Current: {progress.CurrentFile}"), token);
        });

        var result = $"Vectorized {response.FileCount} files into collection `{response.Collection}` with {response.ChunkCount} chunks.";
        await context.ReportAsync(new BackgroundJobProgress(response.FileCount, response.FileCount, result, Result: result), cancellationToken);
        return result;
    }
}
