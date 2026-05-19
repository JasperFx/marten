#nullable enable
using System.Collections.Generic;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;

namespace Marten.Events;

/// <summary>
/// Plumbing for ensuring <see cref="IEvent.TenantId"/> reflects the owning
/// <see cref="StreamAction.TenantId"/> by the time inline projections run.
///
/// <para>
/// JasperFx.Events' <c>StreamAction.applyRichMetadata</c> /
/// <c>applyQuickMetadata</c> assign <c>event.TenantId = session.TenantId</c>.
/// When a caller appends via <c>session.ForTenant("X").Events.StartStream(...)</c>
/// from a session opened on a different tenant, the stream is correctly tagged
/// with the override tenant but the in-memory events end up with the session
/// tenant. Storage is fine (the events row reads from
/// <see cref="StreamAction.TenantId"/> via <c>TenantIdColumn</c>), but the
/// inline multi-stream slicer groups events by <c>event.TenantId</c> and
/// routes the projected document to the wrong tenant. Tracked in
/// jasperfx/marten#4424.
/// </para>
///
/// <para>
/// The fix swaps the <see cref="IMetadataContext"/> handed to
/// <c>StreamAction.PrepareEvents</c> for a wrapper whose <c>TenantId</c> is
/// the stream's tenant — but only when events haven't already been explicitly
/// tagged. That preserves <c>GlobalEventAppenderDecorator</c>'s behavior
/// (which deliberately sets <c>stream.TenantId = default</c> while keeping
/// <c>event.TenantId = originalTenantId</c> for global projections, jasperfx/marten#4270).
/// </para>
/// </summary>
internal static class TenantPropagation
{
    /// <summary>
    /// Returns the metadata context to pass to <see cref="StreamAction.PrepareEvents"/>
    /// for <paramref name="stream"/>. When the stream's tenant differs from
    /// the session tenant AND no other component has set
    /// <see cref="IEvent.TenantId"/> on the stream's events, returns a wrapper
    /// that defaults the events to <see cref="StreamAction.TenantId"/>.
    /// Otherwise returns <paramref name="session"/> unchanged.
    /// </summary>
    internal static IMetadataContext MetadataContextFor(IMetadataContext session, StreamAction stream)
    {
        if (stream.TenantId.IsEmpty() || stream.TenantId == session.TenantId)
        {
            return session;
        }

        // If any event has a non-empty TenantId that differs from the stream,
        // some caller (e.g. GlobalEventAppenderDecorator preserving the
        // session-tenant for global single-stream projections, #4270) has
        // explicitly tagged it. Leave it alone — using the wrapper would route
        // applyQuickMetadata back to the stream tenant and erase that
        // preservation. The applyRichMetadata path already respects non-empty
        // event tenants on its own.
        //
        // Note: StreamAction.TenantId's setter propagates to events, so
        // event.TenantId == stream.TenantId is the natural "no explicit
        // override" state and DOES want the wrapper (so applyQuickMetadata
        // doesn't clobber the propagated value).
        foreach (var @event in stream.Events)
        {
            if (!@event.TenantId.IsEmpty() && @event.TenantId != stream.TenantId)
            {
                return session;
            }
        }

        return new StreamTenantMetadataContext(session, stream.TenantId);
    }
}

/// <summary>
/// <see cref="IMetadataContext"/> wrapper that reports a caller-supplied
/// <c>TenantId</c> while delegating every other member to the underlying
/// session. Used by <see cref="TenantPropagation.MetadataContextFor"/> to make
/// in-memory event metadata pick up <see cref="StreamAction.TenantId"/>
/// instead of the session's tenant.
/// </summary>
internal sealed class StreamTenantMetadataContext: IMetadataContext
{
    private readonly IMetadataContext _inner;

    public StreamTenantMetadataContext(IMetadataContext inner, string tenantId)
    {
        _inner = inner;
        TenantId = tenantId;
    }

    public string TenantId { get; }

    public string? CausationId
    {
        get => _inner.CausationId;
        set => _inner.CausationId = value;
    }

    public string? CorrelationId
    {
        get => _inner.CorrelationId;
        set => _inner.CorrelationId = value;
    }

    public string? CurrentUserName
    {
        get => _inner.CurrentUserName;
        set => _inner.CurrentUserName = value;
    }

    public Dictionary<string, object>? Headers => _inner.Headers;

    public bool CausationIdEnabled => _inner.CausationIdEnabled;

    public bool CorrelationIdEnabled => _inner.CorrelationIdEnabled;

    public bool HeadersEnabled => _inner.HeadersEnabled;

    public bool UserNameEnabled => _inner.UserNameEnabled;
}
