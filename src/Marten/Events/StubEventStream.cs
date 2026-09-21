namespace Marten.Events;

/// <summary>
/// A testing standin fake for IEventStream that might be helpful in
/// unit testing.
/// </summary>
/// <remarks>
/// <para>
/// #5458: this is now a thin subclass of <see cref="JasperFx.Events.StubEventStream{T}" />, which
/// reads identically across Marten, Polecat and Fisher. Everything except the
/// <see cref="StoreOptions" /> overload below is inherited, so existing tests keep compiling and
/// there is one implementation to maintain. New tests should prefer the JasperFx type directly
/// unless they need Marten-specific event type aliases; this one is expected to be marked obsolete
/// on the next major.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate type</typeparam>
public class StubEventStream<T>: JasperFx.Events.StubEventStream<T> where T : notnull
{
    /// <summary>
    /// Start from an existing aggregate -- or null for a stream that does not exist yet
    /// </summary>
    /// <param name="aggregate"></param>
    public StubEventStream(T? aggregate): base(aggregate, new StoreOptions().EventGraph)
    {
    }

    /// <summary>
    /// Start from an existing aggregate and a configuration for Marten.
    /// You only care about this overload if you are customizing event
    /// type aliases
    /// </summary>
    /// <param name="aggregate"></param>
    /// <param name="options"></param>
    /// <remarks>
    /// #5458: this used to build a brand new <see cref="EventGraph" /> from the options, which has
    /// its own empty event type cache -- so an alias registered through
    /// <c>options.Events.MapEventType&lt;T&gt;("...")</c> never reached the stream, and
    /// <see cref="JasperFx.Events.StubEventStream{T}.Events" /> reported the conventional name
    /// anyway. It now uses the options' own graph, which is the one the aliases were configured on.
    /// </remarks>
    public StubEventStream(T? aggregate, StoreOptions options): base(aggregate, options.EventGraph)
    {
    }
}
