using System;
using System.Data;
using NpgsqlTypes;

namespace Marten.Schema.Sequences
{
    public class HiLoSequence : ISequence
    {
        private readonly string _entityName;
        private readonly object _lock = new object();
        private readonly ICommandRunner _runner;

        public HiLoSequence(ICommandRunner runner, string entityName, HiloDef def)
        {
            _runner = runner;
            _entityName = entityName;

            CurrentHi = -1;
            CurrentLo = 1;
            MaxLo = def.MaxLo;
            Increment = def.Increment;
        }

        public string EntityName => _entityName;

        public long CurrentHi { get; private set; }
        public int CurrentLo { get; private set; }

        public int MaxLo { get; }
        public int Increment { get; private set; }

        public int NextInt()
        {
            return (int) NextLong();
        }

        public long NextLong()
        {
            lock (_lock)
            {
                if (ShouldAdvanceHi())
                {
                    AdvanceToNextHi();
                }

                return AdvanceValue();
            }
        }

        public void AdvanceToNextHi()
        {
            _runner.Execute(conn =>
            {
                using (var tx = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    var command = conn.CreateCommand();
                    command.Transaction = tx;
                    command.CommandType = CommandType.StoredProcedure;
                    command.CommandText = "mt_get_next_hi";
                    command.Parameters.Add("entity", NpgsqlDbType.Varchar).Value = _entityName;
                    var nextParam = command.Parameters.Add("next", NpgsqlDbType.Bigint);
                    nextParam.Direction = ParameterDirection.ReturnValue;

                    var raw = command.ExecuteScalar();
                    CurrentHi = Convert.ToInt64(raw);

                    tx.Commit();
                }
            });

            CurrentLo = 1;
        }

        public long AdvanceValue()
        {
            var result = (CurrentHi*MaxLo) + CurrentLo;
            CurrentLo++;

            return result;
        }


        public bool ShouldAdvanceHi()
        {
            return CurrentHi < 0 || CurrentLo > MaxLo;
        }
    }
}