namespace LLLMax.Api.BackgroundJobs;

public interface IBackgroundJobHandler
{
    string Kind { get; }

    Task<string> RunAsync(BackgroundJob job, IBackgroundJobContext context, CancellationToken cancellationToken);
}

public interface IBackgroundJobContext
{
    Task ReportAsync(BackgroundJobProgress progress, CancellationToken cancellationToken);
}
