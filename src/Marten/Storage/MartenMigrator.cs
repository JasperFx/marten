#nullable enable
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Marten.Storage;

/// <summary>
///     Marten's <see cref="PostgresqlMigrator" />: after a migration that created an extension, it reloads the
///     data source's types.
/// </summary>
/// <remarks>
///     Npgsql loads a data source's type catalog when its first physical connection opens, and a migration
///     needs a connection before it can create anything. So a type an extension adds (<c>hstore</c> for
///     <c>DcbStorageMode.HStore</c>) was unknown to that data source until the process restarted, and the first
///     append failed with "The data type name '_hstore' ... could not be found". Marten's own data source
///     bootstraps the extension in a physical-connection initializer, but a data source the application supplies
///     through <c>opts.Connection(NpgsqlDataSource)</c> cannot be given one.
/// </remarks>
internal sealed class MartenMigrator: PostgresqlMigrator
{
    internal static bool CreatesAnExtension(SchemaMigration migration) =>
        migration.Deltas.Any(x => x.SchemaObject is Extension && x.Difference != SchemaPatchDifference.None);

    protected override async Task executeDelta(SchemaMigration migration, DbConnection conn, AutoCreate autoCreate,
        IMigrationLogger logger, CancellationToken ct = default)
    {
        await base.executeDelta(migration, conn, autoCreate, logger, ct).ConfigureAwait(false);

        // Connections take the new types the next time they are opened from the pool
        if (CreatesAnExtension(migration) && conn is NpgsqlConnection npgsql)
        {
            await npgsql.ReloadTypesAsync(ct).ConfigureAwait(false);
        }
    }
}
