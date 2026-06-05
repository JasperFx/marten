using Marten.ScaleTesting.Domain;

namespace Marten.ScaleTesting.Seeding;

/// <summary>
/// Per-tenant batch interleaver. Holds the in-progress event sequence for
/// every stream that hasn't yet drained and emits one <see cref="EventBatch"/>
/// at a time, drawn from a stream chosen at random with weights tuned for a
/// realistic Telehealth event mix.
///
/// <para>
/// The weights are blended Appointment-heavy because that's the production
/// reality (boards exist for the day, providers for the shift, but
/// appointments churn constantly). Tune from <see cref="StreamWeights"/> if
/// the harness ever needs a different stress shape.
/// </para>
/// </summary>
internal sealed class EventInterleaver
{
    public sealed record StreamWeights(double Appointment, double Board, double ProviderShift)
    {
        public static readonly StreamWeights Default = new(Appointment: 70, Board: 5, ProviderShift: 25);
    }

    private readonly string _tenantId;
    private readonly Random _rng;
    private readonly StreamWeights _weights;
    private readonly List<PendingStream> _appointments = [];
    private readonly List<PendingStream> _boards = [];
    private readonly List<PendingStream> _shifts = [];

    public EventInterleaver(string tenantId, Random rng, StreamWeights? weights = null)
    {
        _tenantId = tenantId;
        _rng = rng;
        _weights = weights ?? StreamWeights.Default;
    }

    public void AddAppointmentStream(Guid streamId, List<object> events) => add(_appointments, streamId, typeof(Appointment), events);
    public void AddBoardStream(Guid streamId, List<object> events) => add(_boards, streamId, typeof(Board), events);
    public void AddProviderShiftStream(Guid streamId, List<object> events) => add(_shifts, streamId, typeof(ProviderShift), events);

    private static void add(List<PendingStream> bucket, Guid streamId, Type aggregateType, List<object> events)
    {
        if (events.Count == 0) return;
        bucket.Add(new PendingStream(streamId, aggregateType, events));
    }

    /// <summary>
    /// Drains all queued streams as <see cref="EventBatch"/> emissions, one
    /// batch per stream (each batch is the complete stream). The weighted
    /// random pick across stream types still produces cross-stream
    /// interleaving at the events-table level once writers commit, without
    /// any per-stream version-sequence races.
    /// </summary>
    public IEnumerable<EventBatch> Drain()
    {
        while (_appointments.Count > 0 || _boards.Count > 0 || _shifts.Count > 0)
        {
            var bucket = pickBucket();
            if (bucket.Count == 0) continue; // weighted pick can land on an empty bucket; retry.

            var idx = _rng.Next(bucket.Count);
            var stream = bucket[idx];
            bucket.RemoveAt(idx);

            yield return new EventBatch(_tenantId, stream.StreamId, stream.AggregateType, stream.Events);
        }
    }

    /// <summary>
    /// Weighted pick across the three bucket types. Returns the bucket the
    /// RNG selected; caller must handle the empty-bucket case (drained
    /// streams get pruned out of band).
    /// </summary>
    private List<PendingStream> pickBucket()
    {
        // Skip drained buckets so the weighted draw doesn't waste rolls
        // late in seeding when one type runs out.
        var total = 0.0;
        if (_appointments.Count > 0) total += _weights.Appointment;
        if (_boards.Count > 0) total += _weights.Board;
        if (_shifts.Count > 0) total += _weights.ProviderShift;

        var roll = _rng.NextDouble() * total;
        if (_appointments.Count > 0)
        {
            if (roll < _weights.Appointment) return _appointments;
            roll -= _weights.Appointment;
        }
        if (_boards.Count > 0)
        {
            if (roll < _weights.Board) return _boards;
            roll -= _weights.Board;
        }
        return _shifts;
    }

    private sealed record PendingStream(Guid StreamId, Type AggregateType, List<object> Events);
}
