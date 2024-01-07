using System.Threading.Channels;
using DispatchCore.Core.Models;

namespace DispatchCore.Executor;

public sealed class JobChannel
{
    private readonly Channel<JobEnvelope> _channel;

    public JobChannel(int capacity = 100)
    {
        _channel = Channel.CreateBounded<JobEnvelope>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public ChannelWriter<JobEnvelope> Writer => _channel.Writer;
    public ChannelReader<JobEnvelope> Reader => _channel.Reader;
}
