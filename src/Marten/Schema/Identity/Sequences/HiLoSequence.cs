using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Weasel.Postgresql;
using Marten.Storage;
using Marten.Util;
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
            await AdvanceToNextHi();

            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync();

            await conn.CreateCommand(updateSql)
                .With("floor", numberOfPages)
                .With("name", _entityName)
                .ExecuteNonQueryAsync();

            // And again to get it where we need it to be
            await AdvanceToNextHi();
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
                    AdvanceToNextHi().GetAwaiter().GetResult();
                }
                return AdvanceValue();
            }
        }

        public async Task AdvanceToNextHi()
        {
            using var conn = _tenant.CreateConnection();
            await conn.OpenAsync();

            try
            {
                var attempts = 0;
                do
                {
                    attempts++;

                    // Sproc is expected to return -1 if it's unable to
                    // atomically secure the next hi
                    var raw = await conn.CreateCommand().CallsSproc(GetNextFunction)
                        .With("entity", _entityName)
                        .Returns("next", NpgsqlDbType.Bigint).ExecuteScalarAsync();

                    CurrentHi = Convert.ToInt64(raw);

                } while (CurrentHi < 0 && attempts < _settings.MaxAdvanceToNextHiAttempts);

                // if CurrentHi is still less than 0 at this point, then throw exception
                if (CurrentHi < 0)
                {
                    throw new HiloSequenceAdvanceToNextHiAttemptsExceededException();
                }
            }
            finally
            {
                await conn.CloseAsync();
                conn.Dispose();
            }

            CurrentLo = 1;
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
