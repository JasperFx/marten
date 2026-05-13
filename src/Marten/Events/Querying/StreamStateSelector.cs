using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;

namespace Marten.Events.Querying;

/// <summary>
///     Internal base class for generated stream state query handling. Only the
///     <c>ConfigureCommand</c> step (which varies by <see cref="JasperFx.Events.StreamIdentity"/>
///     and <see cref="Marten.Storage.TenancyStyle"/>) is codegen'd; the row read is delegated
///     to <see cref="IEventStorage"/>'s <see cref="ISelector{StreamState}"/> implementation so
///     <see cref="DocumentStore"/>'s <c>FetchStreamStateAsync</c> shares the same reader as
///     any other call site (e.g. the <c>IEventStore</c> explorer) that needs a
///     <see cref="StreamState"/> out of a raw <see cref="DbDataReader"/>.
/// </summary>
public abstract class StreamStateQueryHandler: IQueryHandler<StreamState>
{
    public abstract void ConfigureCommand(ICommandBuilder builder, IMartenSession session);

    public StreamState Handle(DbDataReader reader, IMartenSession session)
    {
        if (!reader.Read()) return null;
        var selector = (ISelector<StreamState>)session.EventStorage();
        return selector.Resolve(reader);
    }

    public async Task<StreamState> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        var selector = (ISelector<StreamState>)session.EventStorage();
        return await selector.ResolveAsync(reader, token).ConfigureAwait(false);
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException();
    }
}
