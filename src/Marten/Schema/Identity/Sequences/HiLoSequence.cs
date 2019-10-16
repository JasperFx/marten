using System;
using System.Data;
using System.Threading;
using Marten.Storage;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema.Identity.Sequences
{
    public class HiloSequence: ISequence
    {
        private readonly ITenant _tenant;
        private readonly StoreOptions _options;
        private readonly string _entityName;
        private readonly object _lock = new object();

        private DbObjectName GetNextFunction => new DbObjectName(_options.DatabaseSchemaName, "mt_get_next_hi");

        public HiloSequence(ITenant tenant, StoreOptions options, string entityName, HiloSettings settings)
        {
            _tenant = tenant;
            _options = options;
            _entityName = entityName;
            CurrentHi = -1;
            CurrentLo = 1;
            MaxLo = settings.MaxLo;
        }

        public string EntityName => _entityName;

        public long CurrentHi { get; private set; }
        public int CurrentLo { get; private set; }

        public int MaxLo { get; }

        public void SetFloor(long floor)
        {
            var numberOfPages = (long)Math.Ceiling((double)floor / MaxLo);
            var updateSql =
                $"update {_options.DatabaseSchemaName}.mt_hilo set hi_value = :floor where entity_name = :name";

            // This guarantees that the hilo row exists
            AdvanceToNextHi();

            using (var conn = _tenant.CreateConnection())
            {
                conn.Open();

                conn.CreateCommand(updateSql)
                    .With("floor", numberOfPages)
                    .With("name", _entityName)
                    .ExecuteNonQuery();
            }

            // And again to get it where we need it to be
            AdvanceToNextHi();
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
                    try
                    {
                        AdvanceToNextHi();
                    }
                    catch (Exception)
                    {
                        // Retry once.
                        Thread.Sleep(50);
                        AdvanceToNextHi();
                    }
                }

                return AdvanceValue();
            }
        }

        public void AdvanceToNextHi()
        {
            using (var conn = _tenant.CreateConnection())
            {
                conn.Open();

                try
                {
                    var tx = conn.BeginTransaction(IsolationLevel.Serializable);
                    var raw = conn.CreateCommand().CallsSproc(GetNextFunction)
                        .With("entity", _entityName)
                        .Returns("next", NpgsqlDbType.Bigint).ExecuteScalar();

                    tx.Commit();

                    CurrentHi = Convert.ToInt64(raw);
                }
                finally
                {
                    conn.Close();
                    conn.Dispose();
                }
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
