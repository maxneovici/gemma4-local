using System.Threading.Channels;

namespace LLLMax.Api.BackgroundJobs;

public sealed class ChannelBackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public async ValueTask EnqueueAsync(string jobId, CancellationToken cancellationToken) =>
        await _channel.Writer.WriteAsync(jobId, cancellationToken);

    public async ValueTask<string> DequeueAsync(CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken);
}
