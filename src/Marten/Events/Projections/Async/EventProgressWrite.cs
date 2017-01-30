using System;
using System.Text;
using Marten.Schema;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events.Projections.Async
{
    public class EventProgressWrite : IStorageOperation
    {
        private readonly string _key;
        private readonly long _number;
        private NpgsqlParameter _nameArg;
        private NpgsqlParameter _numberArg;
        private readonly FunctionName _sproc;

        public EventProgressWrite(EventGraph events, string key, long number)
        {
            _sproc = new FunctionName(events.DatabaseSchemaName, "mt_mark_event_progression");
            _key = key;
            _number = number;
        }

        public void WriteToSql(StringBuilder builder)
        {
            builder.Append($"select {_sproc}(:{_nameArg.ParameterName}, :{_numberArg.ParameterName})");
        }

        public void AddParameters(IBatchCommand batch)
        {
            _nameArg = batch.AddParameter(_key, NpgsqlDbType.Varchar);
            _numberArg = batch.AddParameter(_number, NpgsqlDbType.Bigint);
        }

        public Type DocumentType => null;
    }
}