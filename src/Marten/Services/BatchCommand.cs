using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Baseline;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Services
{
    public class BatchCommand
    {
        private int _counter = 0;
        private readonly NpgsqlCommand _command = new NpgsqlCommand();
        private readonly IList<ICall> _calls = new List<ICall>();
        private readonly ISerializer _serializer;

        public BatchCommand(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public int Count => _calls.Count;

        private NpgsqlParameter addParameter(object value, NpgsqlDbType dbType)
        {
            var name = "p" + _counter++;
            var param = _command.AddParameter(name, value);
            param.NpgsqlDbType = dbType;

            return param;
        }

        public NpgsqlCommand BuildCommand()
        {
            var builder = new StringBuilder();
            _calls.Each(x =>
            {
                x.WriteToSql(builder);
                builder.Append(";");
            });

            _command.CommandText = builder.ToString();

            return _command;
        }

        public interface ICall
        {
            void WriteToSql(StringBuilder builder);
        }

        public class DeleteCall : ICall
        {
            private readonly string _table;
            private readonly string _idParam;

            public DeleteCall(string table, string idParam)
            {
                _table = table;
                _idParam = idParam;
            }


            public void WriteToSql(StringBuilder builder)
            {
                builder.AppendFormat("delete from {0} where id=:{1}", _table, _idParam);
            }
        }

        public class SprocCall : ICall
        {
            private readonly BatchCommand _parent;
            private readonly string _sprocName;
            private readonly IList<string> _paramNames = new List<string>();


            public SprocCall(BatchCommand parent, string sprocName)
            {
                _parent = parent;
                _sprocName = sprocName;
            }

            public void WriteToSql(StringBuilder builder)
            {
                var parameters = _paramNames.Select(x => ":" + x).Join(", ");
                builder.AppendFormat("select {0}({1})", _sprocName, parameters);
            }

            public SprocCall Param(Guid value)
            {
                return Param(value, NpgsqlDbType.Uuid);
            }

            public SprocCall Param(string value)
            {
                return Param(value, NpgsqlDbType.Varchar);
            }

            public SprocCall JsonEntity(object value)
            {
                var json = _parent._serializer.ToJson(value);
                return Param(json, NpgsqlDbType.Jsonb);
            }

            public SprocCall JsonBody(string json)
            {
                return Param(json, NpgsqlDbType.Jsonb);
            }

            public SprocCall Param(object value, NpgsqlDbType dbType)
            {
                var param = _parent.addParameter(value, dbType);

                _paramNames.Add(param.ParameterName);

                return this;
            }

        }

        public SprocCall Sproc(string name)
        {
            var call = new SprocCall(this, name);
            _calls.Add(call);

            return call;
        }

        public void Delete(string tableName, object id, NpgsqlDbType dbType)
        {
            var param = addParameter(id, dbType);
            var call = new DeleteCall(tableName, param.ParameterName);
            _calls.Add(call);
        }
    }
}