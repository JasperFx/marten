using System.Diagnostics;

namespace Marten.ScaleTesting.Instrumentation;

/// <summary>
/// Counts the total Npgsql command executions during a rebuild scope by subscribing to the
/// <c>Npgsql</c> ActivitySource (Npgsql ≥ 8 emits one Activity per command, aligning with
/// OpenTelemetry's <c>db.*</c> semantic conventions). Subscribing via <see cref="ActivityListener"/>
/// avoids poking at Npgsql internals; the histogram-style Meter instruments would record durations
/// rather than counts, which is the wrong primitive for "how many round-trips."
/// </summary>
internal sealed class NpgsqlCommandCounter : IDisposable
{
    private readonly ActivityListener _listener;
    private long _count;
    private bool _disposed;

    private NpgsqlCommandCounter()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
            ActivityStopped = OnStopped
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public static NpgsqlCommandCounter Start() => new();

    private void OnStopped(Activity activity)
    {
        // One Activity per command (Execute / ExecuteReader / ExecuteScalar / etc). Filter by
        // OperationName so connection-open / connection-close spans don't inflate the count.
        // Npgsql tags command spans with kind=Client and emits them under operation names like
        // "Npgsql.OpenConnection" vs the actual SQL operation; the SQL operations are the ones
        // we care about and they carry a DisplayName matching the command text.
        if (activity.Source.Name != "Npgsql") return;
        if (activity.OperationName == "Npgsql.OpenConnection"
            || activity.OperationName == "Npgsql.CloseConnection") return;
        Interlocked.Increment(ref _count);
    }

    public long SnapshotCount() => Interlocked.Read(ref _count);

    public long Stop()
    {
        var final = SnapshotCount();
        Dispose();
        return final;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener.Dispose();
    }
}
