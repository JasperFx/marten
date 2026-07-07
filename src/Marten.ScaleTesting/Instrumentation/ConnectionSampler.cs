using System.Globalization;
using Npgsql;

namespace Marten.ScaleTesting.Instrumentation;

/// <summary>
/// Background poll loop that snapshots <c>pg_stat_activity</c> for the connection footprint of a
/// single application name (the store under test stamps its own <c>Application Name</c>, so the
/// sampler's own session and unrelated dev-box traffic are excluded). This is the WS2
/// (jasperfx#486) measurement primitive: peak/mean concurrent connections while the daemon runs.
/// Mirrors the sampling shape of <see cref="ProgressionLockSampler"/>.
/// </summary>
internal sealed class ConnectionSampler: IAsyncDisposable
{
    private const string SqlText = @"
SELECT
    count(*) AS total,
    count(*) FILTER (WHERE state <> 'idle') AS busy
FROM pg_stat_activity
WHERE datname = current_database()
  AND pid <> pg_backend_pid()
  AND application_name = @app;";

    private readonly string _connectionString;
    private readonly string _applicationName;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts;
    private readonly Task _loop;
    private readonly List<ConnectionSample> _samples = new();
    private readonly StreamWriter? _traceWriter;

    private ConnectionSampler(string connectionString, string applicationName, TimeSpan interval,
        string? tracePath, CancellationToken outerCancellation)
    {
        _connectionString = connectionString;
        _applicationName = applicationName;
        _interval = interval;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        if (tracePath != null)
        {
            // AutoFlush so a long-running scenario's trace can be tailed live
            _traceWriter = new StreamWriter(tracePath, append: false) { AutoFlush = true };
            _traceWriter.WriteLine("timestamp,total_connections,busy_connections");
        }

        _loop = Task.Run(LoopAsync, CancellationToken.None);
    }

    public static ConnectionSampler Start(string connectionString, string applicationName,
        TimeSpan interval, string? tracePath, CancellationToken cancellation)
        => new(connectionString, applicationName, interval, tracePath, cancellation);

    public sealed record ConnectionSample(DateTimeOffset Timestamp, int Total, int Busy);

    public sealed record Snapshot(int SampleCount, int MaxTotal, double MeanTotal, int MaxBusy, double MeanBusy);

    public Snapshot Capture()
    {
        lock (_samples)
        {
            if (_samples.Count == 0)
            {
                return new Snapshot(0, 0, 0, 0, 0);
            }

            return new Snapshot(
                _samples.Count,
                _samples.Max(x => x.Total),
                _samples.Average(x => x.Total),
                _samples.Max(x => x.Busy),
                _samples.Average(x => x.Busy));
        }
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await SampleOnceAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Transient sampling failure — skip the sample, keep the loop alive
            }

            try
            {
                await Task.Delay(_interval, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SampleOnceAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(_cts.Token).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(SqlText, conn);
        cmd.Parameters.AddWithValue("app", _applicationName);

        await using var reader = await cmd.ExecuteReaderAsync(_cts.Token).ConfigureAwait(false);
        if (!await reader.ReadAsync(_cts.Token).ConfigureAwait(false))
        {
            return;
        }

        var sample = new ConnectionSample(
            DateTimeOffset.UtcNow,
            Convert.ToInt32(reader.GetValue(0)),
            Convert.ToInt32(reader.GetValue(1)));

        lock (_samples)
        {
            _samples.Add(sample);
        }

        if (_traceWriter != null)
        {
            await _traceWriter.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                    $"{sample.Timestamp:O},{sample.Total},{sample.Busy}"))
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch
        {
            // Best-effort teardown
        }

        if (_traceWriter != null)
        {
            await _traceWriter.FlushAsync().ConfigureAwait(false);
            _traceWriter.Dispose();
        }

        _cts.Dispose();
    }
}
