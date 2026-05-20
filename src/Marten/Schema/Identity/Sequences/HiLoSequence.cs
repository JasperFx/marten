#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Core.Sequences;
using Weasel.Postgresql;

namespace Marten.Schema.Identity.Sequences;

// 9.0 (#4527 dedupe): the Hi-Lo client-side arithmetic + hi/lo state now lives in
// Weasel.Core.Sequences.HiloSequenceBase (line-for-line identical to Marten's prior
// implementation). This subclass keeps only the PostgreSQL I/O — the mt_get_next_hi
// stored function and the mt_hilo floor update.
public class HiloSequence: HiloSequenceBase
{
    private readonly IMartenDatabase _database;
    private readonly StoreOptions _options;

    public HiloSequence(IMartenDatabase database, StoreOptions options, string entityName, HiloSettings settings)
        : base(entityName, settings)
    {
        _database = database;
        _options = options;
    }

    private DbObjectName GetNextFunction => new PostgresqlObjectName(_options.DatabaseSchemaName, "mt_get_next_hi");

    public override async Task SetFloor(long floor)
    {
        var numberOfPages = (long)Math.Ceiling((double)floor / MaxLo);
        var updateSql =
            $"update {_options.DatabaseSchemaName}.mt_hilo set hi_value = :floor where entity_name = :name";

        // This guarantees that the hilo row exists
        await AdvanceToNextHi().ConfigureAwait(false);

        await using var conn = _database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await conn.CreateCommand(updateSql)
            .With("floor", numberOfPages)
            .With("name", EntityName)
            .ExecuteNonQueryAsync().ConfigureAwait(false);

        // And again to get it where we need it to be
        await AdvanceToNextHi().ConfigureAwait(false);
    }

    public override async Task AdvanceToNextHi(CancellationToken ct = default)
    {
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        for (var attempts = 0; attempts < Settings.MaxAdvanceToNextHiAttempts; attempts++)
        {
            var command = GetNexFunctionCommand(conn);
            var raw = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);

            if (TrySetCurrentHi(raw))
            {
                return;
            }
        }

        // CurrentHi is still less than 0 at this point, then throw exception
        throw new HiloSequenceAdvanceToNextHiAttemptsExceededException();
    }

    protected override void AdvanceToNextHiSync()
    {
        using var conn = _database.CreateConnection();
        conn.Open();

        for (var attempts = 0; attempts < Settings.MaxAdvanceToNextHiAttempts; attempts++)
        {
            var command = GetNexFunctionCommand(conn);
            var raw = command.ExecuteScalar();

            if (TrySetCurrentHi(raw))
            {
                return;
            }
        }

        // CurrentHi is still less than 0 at this point, then throw exception
        throw new HiloSequenceAdvanceToNextHiAttemptsExceededException();
    }

    private NpgsqlCommand GetNexFunctionCommand(NpgsqlConnection conn)
    {
        // Sproc is expected to return -1 if it's unable to
        // atomically secure the next hi
        return conn.CallFunction(GetNextFunction, "entity")
            .With("entity", EntityName)
            .Returns("next", NpgsqlDbType.Bigint);
    }
}
