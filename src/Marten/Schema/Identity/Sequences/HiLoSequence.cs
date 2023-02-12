#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Schema.Identity.Sequences;

public class HiloSequence: ISequence
{
    private readonly IMartenDatabase _database;
    private readonly object _lock = new();
    private readonly StoreOptions _options;
    private readonly HiloSettings _settings;

    public HiloSequence(IMartenDatabase database, StoreOptions options, string entityName, HiloSettings settings)
    {
        _database = database;
        _options = options;
        EntityName = entityName;
        CurrentHi = -1;
        CurrentLo = 1;
        MaxLo = settings.MaxLo;

        _settings = settings;
    }

    private DbObjectName GetNextFunction => new(_options.DatabaseSchemaName, "mt_get_next_hi");

    public string EntityName { get; }

    public long CurrentHi { get; private set; }
    public int CurrentLo { get; private set; }

    public int MaxLo { get; }

    public async Task SetFloor(long floor)
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

    public int NextInt()
    {
        return (int)NextLong();
    }

    public long NextLong()
    {
        lock (_lock)
        {
            if (ShouldAdvanceHi())
            {
                AdvanceToNextHiSync();
            }

            return AdvanceValue();
        }
    }

    public async Task AdvanceToNextHi(CancellationToken ct = default)
    {
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        for (var attempts = 0; attempts < _settings.MaxAdvanceToNextHiAttempts; attempts++)
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

    public void AdvanceToNextHiSync()
    {
        using var conn = _database.CreateConnection();
        conn.Open();

        for (var attempts = 0; attempts < _settings.MaxAdvanceToNextHiAttempts; attempts++)
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

    private bool TrySetCurrentHi(object? raw)
    {
        CurrentHi = Convert.ToInt64(raw);

        if (0 <= CurrentHi)
        {
            CurrentLo = 1;
            return true;
        }

        return false;
    }

    private NpgsqlCommand GetNexFunctionCommand(NpgsqlConnection conn)
    {
        // Sproc is expected to return -1 if it's unable to
        // atomically secure the next hi
        return conn.CallFunction(GetNextFunction, "entity")
            .With("entity", EntityName)
            .Returns("next", NpgsqlDbType.Bigint);
    }


    public long AdvanceValue()
    {
        var result = (CurrentHi * MaxLo) + CurrentLo;
        CurrentLo++;

        return result;
    }

    public bool ShouldAdvanceHi()
    {
        return CurrentHi < 0 || CurrentLo > MaxLo;
    }
}
