#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.EventModeling;

namespace Marten.Events.EventModeling;

/// <summary>
///     The store-derived Event Model rung (#5394, jasperfx#825), reading its model NAME off the
///     store's own <see cref="StoreOptions.EventModelName" /> rather than being told at registration.
/// </summary>
/// <remarks>
///     <para>
///         #5405. The name used to be an optional <c>eventModelName</c> parameter on every
///         <c>AddMarten</c> / <c>AddMartenStore&lt;T&gt;</c> overload. That broke binary compatibility
///         (optional arguments bind at the call site) and, on the parameterless overload, silently
///         stole every <c>AddMarten(connectionString)</c> call. Configuration that belongs to a store
///         goes on <see cref="StoreOptions" />; see "API compatibility" in CLAUDE.md.
///     </para>
///     <para>
///         Resolving the store inside <see cref="TryCreateAsync" /> rather than capturing a name when
///         the service is registered also retires the ordering caveat the parameter came with: the
///         options are read when the model is assembled, so <c>AddEventModel("Something", …)</c> may
///         be called before or after <c>AddMarten</c> and either way lands on one model.
///     </para>
///     <para>
///         The mapping itself stays JasperFx's. This delegates to a
///         <see cref="ProjectionEventModelSource" /> — which is <c>sealed</c>, hence composition
///         rather than a subclass — so Marten owns only the two things it alone knows: which service
///         the store is registered under, and where its name came from.
///     </para>
/// </remarks>
internal sealed class MartenProjectionEventModelSource: IEventModelDefinitionSource
{
    private readonly Func<IServiceProvider, IDocumentStore> _store;

    public MartenProjectionEventModelSource(Func<IServiceProvider, IDocumentStore> store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Uri Subject { get; } = new("event-model://projections");

    /// <inheritdoc />
    /// <remarks>
    ///     Derived, not declared: these roles are read out of the store's projection registry rather
    ///     than written down by anybody.
    /// </remarks>
    public EventModelProvenance Provenance => EventModelProvenance.Derived;

    public Task<EventModelDescriptor?> TryCreateAsync(IServiceProvider services, CancellationToken token)
    {
        var store = _store(services);

        // A store that is not a DocumentStore (a test double, say) simply has no name to offer, and
        // the default model is the right answer for it -- the same answer it got before #5405.
        var modelName = (store as DocumentStore)?.Options.EventModelName
                        ?? ProjectionEventModelSource.DefaultModelName;

        var inner = new ProjectionEventModelSource((IEventStore)store)
        {
            ModelName = modelName, Subject = Subject
        };

        return inner.TryCreateAsync(services, token);
    }
}
