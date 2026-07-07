using System.Globalization;
using Npgsql;

namespace Marten.ScaleTesting.Instrumentation;

/// <summary>
/// Multi-node sibling of <see cref="ShardConnectionSampler"/> for the marten#4883 multi-process
/// daemonload scenarios. Each node process stamps its own <c>Application Name</c>
/// (<c>{base}-node{N}</c>), so grouping <c>pg_stat_activity</c> by
/// <c>(application_name, datname)</c> under a shared prefix yields the connection footprint
/// per node × database — the epic's expectation being total ≈ nodes × O(databases).
/// </summary>
internal sealed class NodeConnectionSampler: IAsyncDisposable
{
    private const string SqlText = @"
SELECT
    application_name,
    datname,
    count(*) AS total,
    count(*) FILTER (WHERE state <> 'idle') AS busy
FROM pg_stat_activity
WHERE pid <> pg_backend_pid()
  AND application_name LIKE @prefix || '%'
GROUP BY application_name, datname;";

    private readonly string _connectionString;
    private readonly string _applicationNamePrefix;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts;
    private readonly Task _loop;
    private readonly List<Sample> _samples = new();
    private readonly StreamWriter? _traceWriter;

    private NodeConnectionSampler(string connectionString, string applicationNamePrefix,
        TimeSpan interval, string? tracePath, CancellationToken outerCancellation)
    {
        _connectionString = connectionString;
        _applicationNamePrefix = applicationNamePrefix;
        _interval = interval;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        if (tracePath != null)
        {
            _traceWriter = new StreamWriter(tracePath, append: false) { AutoFlush = true };
            _traceWriter.WriteLine("timestamp,application_name,database,total_connections,busy_connections");
        }

        _loop = Task.Run(LoopAsync, CancellationToken.None);
    }

    public static NodeConnectionSampler Start(string connectionString, string applicationNamePrefix,
        TimeSpan interval, string? tracePath, CancellationToken cancellation)
        => new(connectionString, applicationNamePrefix, interval, tracePath, cancellation);

    private sealed record Sample(DateTimeOffset Timestamp, IReadOnlyDictionary<(string App, string Db), (int Total, int Busy)> Counts);

    public sealed record NodeDatabaseSnapshot(
        string ApplicationName, string Database, int SampleCount, int MaxTotal, double MeanTotal, int MaxBusy);

    /// <summary>
    /// Roll up per (application_name, database). Means are computed over EVERY sample in the run
    /// (zero when the pair was absent), so a node that died mid-run shows a correspondingly lower
    /// mean rather than a survivor-biased one.
    /// </summary>
    public IReadOnlyList<NodeDatabaseSnapshot> Capture()
    {
        lock (_samples)
        {
            var keys = _samples.SelectMany(s => s.Counts.Keys).Distinct().OrderBy(k => k.App).ThenBy(k => k.Db).ToArray();
            return keys.Select(key =>
                {
                    var series = _samples
                        .Select(s => s.Counts.TryGetValue(key, out var counts) ? counts : (Total: 0, Busy: 0))
                        .ToArray();
                    return new NodeDatabaseSnapshot(
                        key.App,
                        key.Db,
                        series.Length,
                        series.Max(x => x.Total),
                        series.Average(x => x.Total),
                        series.Max(x => x.Busy));
                })
                .ToArray();
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
        cmd.Parameters.AddWithValue("prefix", _applicationNamePrefix);

        var counts = new Dictionary<(string, string), (int, int)>();
        await using (var reader = await cmd.ExecuteReaderAsync(_cts.Token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(_cts.Token).ConfigureAwait(false))
            {
                counts[(reader.GetString(0), reader.GetString(1))] =
                    (Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)));
            }
        }

        var sample = new Sample(DateTimeOffset.UtcNow, counts);
        lock (_samples)
        {
            _samples.Add(sample);
        }

        if (_traceWriter != null)
        {
            foreach (var ((app, db), (total, busy)) in counts.OrderBy(x => x.Key))
            {
                await _traceWriter.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                        $"{sample.Timestamp:O},{app},{db},{total},{busy}"))
                    .ConfigureAwait(false);
            }
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
