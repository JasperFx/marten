using System;
using System.Data;
using System.Diagnostics;
using FubuCore;
using NpgsqlTypes;

namespace Marten.Schema.Sequences
{
    public interface ISequences
    {
        ISequence HiLo(Type documentType, HiloDef def);
    }

    public class SequenceFactory : ISequences
    {
        private readonly IDocumentSchemaCreation _creation;
        private readonly CommandRunner _runner;
        private readonly IDocumentSchema _schema;

        public SequenceFactory(IDocumentSchema schema, CommandRunner runner, IDocumentSchemaCreation creation)
        {
            _schema = schema;
            _runner = runner;
            _creation = creation;
        }

        public ISequence HiLo(Type documentType, HiloDef def)
        {
            throw new NotImplementedException();
        }
    }

    public interface ISequence
    {
        int NextInt();
        long NextLong();
    }

    public class HiloDef
    {
        public int Increment = 1;
        public int MaxLo = 1000;
    }

    public class HiLoSequence : ISequence
    {
        private readonly string _entityName;
        private readonly object _lock = new object();
        private readonly CommandRunner _runner;

        public HiLoSequence(CommandRunner runner, string entityName, HiloDef def)
        {
            _runner = runner;
            _entityName = entityName;

            CurrentHi = -1;
            CurrentLo = 1;
            MaxLo = def.MaxLo;
            Increment = def.Increment;
        }

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