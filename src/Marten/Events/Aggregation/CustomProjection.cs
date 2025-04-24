using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Internal.Sessions;

namespace Marten.Events.Aggregation;

/// <summary>
///     Helpful as a base class for more custom aggregation projections that are not supported
///     by the Single/MultipleStreamProjections -- or if you'd just prefer to use explicit code
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public abstract class CustomProjection<TDoc, TId>: MultiStreamProjection<TDoc, TId>
{
    protected CustomProjection()
    {
        Name = GetType().Name;
    }

    /// <summary>
    ///     Apply any document changes based on the incoming slice of events to the underlying aggregate document
    /// </summary>
    /// <param name="session"></param>
    /// <param name="slice"></param>
    /// <param name="cancellation"></param>
    /// <param name="lifecycle"></param>
    /// <returns></returns>
    [Obsolete("TODO -- different method, add something here")]
    public virtual async ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<TDoc, TId> slice,
        CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
    {
        throw new NotSupportedException("This is no longer supported");
    }


    // public SubscriptionDescriptor Describe()
    // {
    //     var type = Slicer is ISingleStreamSlicer
    //         ? SubscriptionType.SingleStreamProjection
    //         : SubscriptionType.MultiStreamProjection;
    //
    //     var subscriptionDescriptor = new SubscriptionDescriptor(this, type);
    //     subscriptionDescriptor.AddValue("DocumentType", typeof(TDoc));
    //     subscriptionDescriptor.AddValue("IdentityType", typeof(TId).ShortNameInCode());
    //
    //     return subscriptionDescriptor;
    // }
}
