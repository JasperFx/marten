using System;

namespace Marten.Events.Daemon.Coordination;

/// <summary>
///     The decision produced by <see cref="OwnershipTracker" /> for a single polling cycle.
/// </summary>
internal readonly record struct OwnershipReport(bool TallyChanged, bool ShouldWarn, TimeSpan OwnedNothingFor);

/// <summary>
///     Tracks how many projection sets this node actually owns from one leadership polling cycle to the
///     next, and decides when that is worth logging.
/// </summary>
/// <remarks>
///     Split out of <see cref="ProjectionCoordinator" /> purely so the threshold and throttling rules can be
///     unit tested without standing up a DocumentStore. It holds no dependencies beyond a TimeProvider and
///     is only ever touched from the coordinator's single polling loop, so it is deliberately not thread safe.
/// </remarks>
internal class OwnershipTracker
{
    private readonly TimeProvider _timeProvider;

    private int? _lastOwned;
    private int? _lastDenied;
    private DateTimeOffset? _ownsNothingSince;
    private DateTimeOffset? _lastWarning;

    public OwnershipTracker(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <param name="owned">Projection sets whose leadership lock this node holds.</param>
    /// <param name="denied">Projection sets whose lock is held by some other node.</param>
    /// <param name="total">All projection sets this node knows about.</param>
    /// <param name="warningThreshold">
    ///     How long this node may own nothing before warning, or null to never warn.
    /// </param>
    /// <param name="repeatTime">How often to repeat that warning while the condition persists.</param>
    public OwnershipReport Record(int owned, int denied, int total, TimeSpan? warningThreshold, TimeSpan repeatTime)
    {
        var now = _timeProvider.GetUtcNow();

        var tallyChanged = _lastOwned != owned || _lastDenied != denied;
        _lastOwned = owned;
        _lastDenied = denied;

        // Owning nothing is only meaningful when there is something to own. A store with no async
        // projections at all is not a node that has been locked out of its work.
        if (owned > 0 || total == 0)
        {
            _ownsNothingSince = null;
            _lastWarning = null;
            return new OwnershipReport(tallyChanged, false, TimeSpan.Zero);
        }

        _ownsNothingSince ??= now;
        var ownedNothingFor = now.Subtract(_ownsNothingSince.Value);

        if (warningThreshold == null || ownedNothingFor < warningThreshold.Value)
        {
            return new OwnershipReport(tallyChanged, false, ownedNothingFor);
        }

        if (_lastWarning != null && now.Subtract(_lastWarning.Value) < repeatTime)
        {
            return new OwnershipReport(tallyChanged, false, ownedNothingFor);
        }

        _lastWarning = now;
        return new OwnershipReport(tallyChanged, true, ownedNothingFor);
    }
}
