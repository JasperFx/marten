using System.Threading;
using System.Threading.Tasks;
using Polly;

namespace Marten.Events.Daemon.New;

internal class ResilientEventLoader: IEventLoader
{
    private readonly ResiliencePipeline _pipeline;
    private readonly IEventLoader _inner;

    internal record EventLoadExecution(EventRequest Request, IEventLoader Loader)
    {
        public async ValueTask<EventPage> ExecuteAsync(CancellationToken token)
        {
            var results = await Loader.LoadAsync(Request, token).ConfigureAwait(false);
            return results;
        }
    }

    public ResilientEventLoader(ResiliencePipeline pipeline, IEventLoader inner)
    {
        _pipeline = pipeline;
        _inner = inner;
    }

    public Task<EventPage> LoadAsync(EventRequest request, CancellationToken token)
    {
        var execution = new EventLoadExecution(request, _inner);
        return _pipeline.ExecuteAsync(static (x, t) => x.ExecuteAsync(t),
            execution, token).AsTask();
    }
}
