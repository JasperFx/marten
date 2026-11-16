#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Cold;

/// <summary>
///     Where the cold part of event streams lives: the events of a stream up to and including its cold
///     version (<c>mt_streams.cold_version</c>), after the application moved them out of <c>mt_events</c>.
///     Register it with <see cref="ColdEventStorage.UseColdEventStore(IEventStoreOptions, IColdEventStore)" />.
/// </summary>
/// <remarks>
///     Marten only reads through this interface. Which streams go cold, when, and where their events are
///     kept is the application's business; <see cref="ColdEventStorage.MarkStreamCold" />
///     records the boundary. Streams must be identified by string (<c>StreamIdentity.AsString</c>). The
///     session is the one doing the read, so it carries the tenant (<see cref="IQuerySession.TenantId" />) and
///     the database of the stream.
/// </remarks>
[Experimental(ColdEventStorage.DiagnosticId, UrlFormat = ColdEventStorage.DocumentationUrl)]
public interface IColdEventStore
{
    /// <summary>
    ///     The cold events of one stream, oldest first, at most one per version and none above
    ///     <paramref name="throughVersion" />.
    /// </summary>
    IAsyncEnumerable<ColdEvent> ReadAsync(IQuerySession session, string streamKey, long throughVersion,
        CancellationToken token);

    /// <summary>
    ///     The document an inline or async single-stream projection had for this stream when its events went cold
    ///     through <paramref name="coldVersion" />, for an application that moved the projection's stored document
    ///     out together with the events and kept it. Null (the default) folds the cold events through
    ///     <see cref="ReadAsync" /> instead.
    /// </summary>
    /// <remarks>
    ///     Only asked when the projection's stored document is missing, the stream has a cold part, and nothing
    ///     warm lies between that cold part and the events being projected.
    /// </remarks>
    ValueTask<TDoc?> TryLoadSnapshotAsync<TDoc>(IQuerySession session, string streamKey, long coldVersion,
        CancellationToken token) where TDoc : notnull => new(default(TDoc));

    /// <summary>
    ///     The cold events of several streams of the session's tenant at once, for
    ///     <see cref="ColdEventStorage.FetchStreamsWithColdAsync" />. Each event comes with the key of its stream.
    ///     The streams may come in any order and interleave, but each stream's events come oldest first, at most
    ///     one per version and none above its <see cref="ColdStreamRead.ThroughVersion" />.
    /// </summary>
    /// <remarks>
    ///     The default calls <see cref="ReadAsync" /> once per stream. Override it to read all of them in one go.
    /// </remarks>
    async IAsyncEnumerable<(string StreamKey, ColdEvent Event)> ReadManyAsync(IQuerySession session,
        IReadOnlyList<ColdStreamRead> streams, [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var stream in streams)
        {
            await foreach (var @event in ReadAsync(session, stream.StreamKey, stream.ThroughVersion, token)
                               .WithCancellation(token).ConfigureAwait(false))
            {
                yield return (stream.StreamKey, @event);
            }
        }
    }
}

/// <summary>
///     One stream in <see cref="IColdEventStore.ReadManyAsync" />: its key, and the version through which its
///     cold events are wanted.
/// </summary>
[Experimental(ColdEventStorage.DiagnosticId, UrlFormat = ColdEventStorage.DocumentationUrl)]
public sealed record ColdStreamRead(string StreamKey, long ThroughVersion);
