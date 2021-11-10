using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Weasel.Postgresql;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;

#nullable enable
namespace Marten.Schema.Identity.Sequences
{
    public class HiloSequence: ISequence
    {
        private readonly ITenant _tenant;
        private readonly StoreOptions _options;
        private readonly string _entityName;
        private readonly object _lock = new object();
        private readonly HiloSettings _settings;

        private DbObjectName GetNextFunction => new DbObjectName(_options.DatabaseSchemaName, "mt_get_next_hi");

        public HiloSequence(ITenant tenant, StoreOptions options, string entityName, HiloSettings settings)
        {
            _tenant = tenant;
            _options = options;
            _entityName = entityName;
            CurrentHi = -1;
            CurrentLo = 1;
            MaxLo = settings.MaxLo;

            _settings = settings;
        }

        public string EntityName => _entityName;

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

            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync().ConfigureAwait(false);

            await conn.CreateCommand(updateSql)
                .With("floor", numberOfPages)
                .With("name", _entityName)
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
                if(ShouldAdvanceHi())
                {
                    AdvanceToNextHiSync();
                }
                return AdvanceValue();
            }
        }

        public async Task AdvanceToNextHi()
        {
            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync().ConfigureAwait(false);

            for (var attempts = 0; attempts < _settings.MaxAdvanceToNextHiAttempts; attempts++)
            {

                var command = GetNexFunctionCommand(conn);
                var raw = await command.ExecuteScalarAsync().ConfigureAwait(false);

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
            using var conn = _tenant.CreateConnection();
            conn.Open();

            for (var attempts = 0; attempts <  _settings.MaxAdvanceToNextHiAttempts; attempts++)
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
            return conn.CreateCommand().CallsSproc(GetNextFunction)
                .With("entity", _entityName)
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
}
