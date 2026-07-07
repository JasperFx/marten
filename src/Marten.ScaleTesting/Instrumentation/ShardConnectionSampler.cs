using System.Globalization;
using Npgsql;

namespace Marten.ScaleTesting.Instrumentation;

/// <summary>
/// Multi-database sibling of <see cref="ConnectionSampler"/> for the sharded daemonload scenario
/// (marten#4882, epic jasperfx#486 WS6). <c>pg_stat_activity</c> is cluster-wide, so ONE sampling
/// session on the maintenance database sees every shard database's backends; each sample groups
/// the store's Application-Name-attributed connections by <c>datname</c>. The WS6 gate is
/// O(databases): with the 2.22.0 per-database governors, each shard database's peak should mirror
/// the single-DB daemonload result rather than scale with that shard's agent count.
/// </summary>
internal sealed class ShardConnectionSampler: IAsyncDisposable
{
    private const string SqlText = @"
SELECT
    datname,
    count(*) AS total,
    count(*) FILTER (WHERE state <> 'idle') AS busy
FROM pg_stat_activity
WHERE pid <> pg_backend_pid()
  AND application_name = @app
  AND datname = ANY(@dbs)
GROUP BY datname;";

    private readonly string _connectionString;
    private readonly string _applicationName;
    private readonly string[] _databases;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts;
    private readonly Task _loop;
    private readonly List<Sample> _samples = new();
    private readonly StreamWriter? _traceWriter;

    private ShardConnectionSampler(string connectionString, string applicationName, string[] databases,
        TimeSpan interval, string? tracePath, CancellationToken outerCancellation)
    {
        _connectionString = connectionString;
        _applicationName = applicationName;
        _databases = databases;
        _interval = interval;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        if (tracePath != null)
        {
            _traceWriter = new StreamWriter(tracePath, append: false) { AutoFlush = true };
            _traceWriter.WriteLine("timestamp,database,total_connections,busy_connections");
        }

        _loop = Task.Run(LoopAsync, CancellationToken.None);
    }

    public static ShardConnectionSampler Start(string connectionString, string applicationName,
        string[] databases, TimeSpan interval, string? tracePath, CancellationToken cancellation)
        => new(connectionString, applicationName, databases, interval, tracePath, cancellation);

    /// <summary>One poll instant: per-database totals. Databases with zero connections at the
    /// instant are recorded explicitly so means don't skew optimistic.</summary>
    private sealed record Sample(DateTimeOffset Timestamp, IReadOnlyDictionary<string, (int Total, int Busy)> PerDatabase);

    public sealed record DatabaseSnapshot(string Database, int SampleCount, int MaxTotal, double MeanTotal, int MaxBusy, double MeanBusy);

    public IReadOnlyList<DatabaseSnapshot> Capture()
    {
        lock (_samples)
        {
            return _databases
                .Select(db =>
                {
                    var series = _samples
                        .Select(s => s.PerDatabase.TryGetValue(db, out var counts) ? counts : (Total: 0, Busy: 0))
                        .ToArray();
                    if (series.Length == 0)
                    {
                        return new DatabaseSnapshot(db, 0, 0, 0, 0, 0);
                    }

                    return new DatabaseSnapshot(
                        db,
                        series.Length,
                        series.Max(x => x.Total),
                        series.Average(x => x.Total),
                        series.Max(x => x.Busy),
                        series.Average(x => x.Busy));
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
        cmd.Parameters.AddWithValue("app", _applicationName);
        cmd.Parameters.AddWithValue("dbs", _databases);

        var perDatabase = new Dictionary<string, (int, int)>();
        await using (var reader = await cmd.ExecuteReaderAsync(_cts.Token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(_cts.Token).ConfigureAwait(false))
            {
                perDatabase[reader.GetString(0)] =
                    (Convert.ToInt32(reader.GetValue(1)), Convert.ToInt32(reader.GetValue(2)));
            }
        }

        var sample = new Sample(DateTimeOffset.UtcNow, perDatabase);
        lock (_samples)
        {
            _samples.Add(sample);
        }

        if (_traceWriter != null)
        {
            foreach (var db in _databases)
            {
                var (total, busy) = perDatabase.TryGetValue(db, out var counts) ? counts : (0, 0);
                await _traceWriter.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                        $"{sample.Timestamp:O},{db},{total},{busy}"))
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
