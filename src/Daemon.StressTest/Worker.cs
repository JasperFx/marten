using Marten;

namespace Daemon.StressTest;

public class Worker: BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IDocumentStore _store;

    public Worker(ILogger<Worker> logger, IDocumentStore store)
    {
        _logger = logger;
        _store = store;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _store.Advanced.Clean.DeleteAllEventDataAsync();
        await _store.Advanced.Clean.DeleteAllDocumentsAsync();
        await Task.Delay(3000);

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var session = _store.OpenSession($"tenant{Random.Shared.Next(1, 10)}");

            var id = Guid.NewGuid();

            session.Events.StartStream(id, new CreateEventProjectionEvent(id), new UpdateEventProjectionEvent(id));

            await session.SaveChangesAsync();

            _logger.LogInformation("Appended Events with StreamID: {streamId}", id);

            await Task.Delay(100, stoppingToken);
        }
    }
}
