using Marten.ScaleTesting.Domain;

namespace Marten.ScaleTesting.Seeding;

/// <summary>
/// Per-stream generators. Each returns the full ordered event sequence for one
/// stream of the given aggregate; the <see cref="EventInterleaver"/> chops it
/// into <see cref="EventBatch"/> emissions and weaves them with peers under
/// the same tenant.
///
/// Deterministic for a fixed <see cref="Random"/>: callers pass a per-stream
/// RNG seeded from <c>(rootSeed, tenantIndex, streamIndex)</c> so reruns
/// produce byte-identical event payloads.
/// </summary>
internal static class StreamGenerators
{
    private static readonly string[] s_states = ["AZ", "CA", "FL", "GA", "IL", "MA", "NC", "NY", "OH", "PA", "TX", "WA"];
    private static readonly string[] s_specialties = ["CARD", "DERM", "ENT", "GP", "NEURO", "ORTHO", "PED", "PSYCH"];
    private static readonly string[] s_alertCodes = ["LOW_STAFF", "SURGE", "HIGH_WAIT", "ESCALATION"];
    private static readonly string[] s_closeReasons = ["EndOfShift", "PolicyClose", "EmergencyStop"];

    /// <summary>
    /// Generates the Appointment event stream — single happy-path with a small
    /// cancellation tail. 4–8 events per stream, weighted heavily toward
    /// scheduled+started+completed (the realistic mix). About 5% of streams
    /// terminate early with a cancel.
    /// </summary>
    public static List<object> Appointment(Random rng, Guid patientId, Guid boardId, Guid providerId, DateTimeOffset clock)
    {
        var state = s_states[rng.Next(s_states.Length)];
        var specialty = s_specialties[rng.Next(s_specialties.Length)];

        var events = new List<object>(8)
        {
            new AppointmentRequested(patientId, state, specialty),
            new AppointmentRouted(boardId, "ROUTINE")
        };

        // 5% early cancel — exercise the SingleStreamProjection delete path.
        if (rng.NextDouble() < 0.05)
        {
            events.Add(new AppointmentCancelled());
            return events;
        }

        events.Add(new ProviderAssigned(providerId));
        events.Add(new AppointmentEstimated(clock.AddMinutes(rng.Next(5, 120))));
        events.Add(new AppointmentStarted());
        events.Add(new AppointmentCompleted());

        // 30% of completed appointments also emit the external identifier event —
        // exercises the AppointmentByExternalIdentifierProjection's Identity<>
        // routing.
        if (rng.NextDouble() < 0.3)
        {
            events.Add(new AppointmentExternalIdentifierAssigned(Guid.NewGuid(), Guid.NewGuid()));
        }

        return events;
    }

    /// <summary>
    /// Generates the Board event stream — open → some shifts join / drop with
    /// alerts raised in between → finish → close. 6–14 events typical.
    /// </summary>
    public static List<object> Board(Random rng, DateOnly date, DateTimeOffset clock)
    {
        var stateCount = rng.Next(1, 4);
        var states = pickN(rng, s_states, stateCount);
        var specCount = rng.Next(1, 3);
        var specs = pickN(rng, s_specialties, specCount);

        var events = new List<object>(14)
        {
            new BoardOpened($"Board-{Guid.NewGuid():N}".Substring(0, 16), date, clock, states, specs)
        };

        // 1–4 shift adds, optionally an alert raise/clear pair, then finish+close.
        var shiftAdds = rng.Next(1, 5);
        for (var i = 0; i < shiftAdds; i++)
        {
            events.Add(new ShiftAdded(Guid.NewGuid()));
        }

        if (rng.NextDouble() < 0.4)
        {
            var alert = s_alertCodes[rng.Next(s_alertCodes.Length)];
            events.Add(new AlertRaised(alert));
            events.Add(new AlertCleared(alert));
        }

        // 70% finish, 100% close (closed boards are the steady-state).
        if (rng.NextDouble() < 0.7)
        {
            events.Add(new BoardFinished(clock.AddHours(rng.Next(1, 8))));
        }
        events.Add(new BoardClosed(clock.AddHours(rng.Next(8, 12)), s_closeReasons[rng.Next(s_closeReasons.Length)]));

        return events;
    }

    /// <summary>
    /// Generates the ProviderShift event stream — join board → ready/assigned
    /// oscillation → charting → paused → sign-off. 4–10 events.
    /// </summary>
    public static List<object> ProviderShift(Random rng, Guid boardId, Guid providerId)
    {
        var events = new List<object>(10)
        {
            new ProviderJoined(boardId, providerId),
            new ProviderReady()
        };

        // 1–3 appointment cycles per shift.
        var cycles = rng.Next(1, 4);
        for (var i = 0; i < cycles; i++)
        {
            events.Add(new AppointmentAssigned(Guid.NewGuid()));
            events.Add(new ChartingStarted());
            events.Add(new ChartingFinished());
            events.Add(new ProviderReady());
        }

        events.Add(new ProviderPaused());
        events.Add(new ProviderSignedOff());
        return events;
    }

    private static string[] pickN(Random rng, string[] source, int n)
    {
        if (n >= source.Length) return source.ToArray();
        var pool = source.ToList();
        var picked = new string[n];
        for (var i = 0; i < n; i++)
        {
            var idx = rng.Next(pool.Count);
            picked[i] = pool[idx];
            pool.RemoveAt(idx);
        }
        return picked;
    }
}
