using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// Sets <c>search_path</c> on the projection <c>DbContext</c>'s connection each time EF Core opens
/// it, so EF's queries resolve the projection's tables in the store's schema.
///
/// <para>
/// #5601: this replaces a single eager <c>SET search_path</c> issued by
/// <see cref="EfCoreDbContextFactory" /> at construction time. That one call is what forced the
/// connection open -- synchronously, before the caller's first await, holding a second pooled
/// connection for the whole save. Now EF opens the connection when it needs it and closes it after,
/// so the schema has to be re-established per open rather than once per context.
/// </para>
///
/// <para>
/// Both the sync and async openings are handled. The async one is what an inline projection's
/// <c>LoadAsync</c> reaches; the sync one is still live because <c>IProjectionStorage.Delete</c> is a
/// synchronous contract and loads through <c>DbContext.Find</c>.
/// </para>
/// </summary>
internal sealed class SearchPathInterceptor: DbConnectionInterceptor
{
    private readonly string _searchPath;

    public SearchPathInterceptor(string schemaName)
    {
        // Composed once rather than per open. The schema name comes from StoreOptions, not from user
        // input, and is the same value the eager SET interpolated.
        _searchPath = $"SET search_path TO {schemaName}";
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection,
        ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = _searchPath;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = _searchPath;
        command.ExecuteNonQuery();
    }
}
