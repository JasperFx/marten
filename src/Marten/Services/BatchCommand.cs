using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Baseline;
using Marten.Linq;
using Marten.Schema;
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

        public class DeleteWhereCall : ICall
        {
            private readonly TableName _table;
            private readonly string _whereClause;

            public DeleteWhereCall(TableName table, string whereClause)
            {
                if (table == null) throw new ArgumentNullException(nameof(table));

                _table = table;
                _whereClause = whereClause;
            }

            public void WriteToSql(StringBuilder builder)
            {
                builder.AppendFormat("delete from {0} as d where {1}", _table, _whereClause);
            }
        }

        public class DeleteCall : ICall
        {
            private readonly TableName _table;
            private readonly string _idParam;

            public DeleteCall(TableName table, string idParam)
            {
                if (table == null) throw new ArgumentNullException(nameof(table));

                _table = table;
                _idParam = idParam;
            }


            public void WriteToSql(StringBuilder builder)
            {
                builder.AppendFormat("delete from {0} where id=:{1}", _table.QualifiedName, _idParam);
            }
        }

        public class SprocCall : ICall
        {
            private readonly BatchCommand _parent;
            private readonly FunctionName _function;
            private readonly IList<ParameterArg> _parameters = new List<ParameterArg>();


            public SprocCall(BatchCommand parent, FunctionName function)
            {
                if (parent == null) throw new ArgumentNullException(nameof(parent));
                if (function == null) throw new ArgumentNullException(nameof(function));

                _parent = parent;
                _function = function;
            }

            public void WriteToSql(StringBuilder builder)
            {
                var parameters = _parameters.Select(x => x.Declaration()).Join(", ");
                builder.AppendFormat("select {0}({1})", _function.QualifiedName, parameters);
            }

            public SprocCall Param(string argName, Guid value)
            {
                return Param(argName, value, NpgsqlDbType.Uuid);
            }

            public SprocCall Param(string argName, string value)
            {
                return Param(argName, value, NpgsqlDbType.Varchar);
            }

            public SprocCall JsonEntity(string argName, object value)
            {
                var json = _parent._serializer.ToJson(value);
                return Param(argName, json, NpgsqlDbType.Jsonb);
            }

            public SprocCall JsonBody(string argName, string json)
            {
                return Param(argName, json, NpgsqlDbType.Jsonb);
            }

            public SprocCall Param(string argName, object value, NpgsqlDbType dbType)
            {
                var param = _parent.addParameter(value, dbType);

                _parameters.Add(new ParameterArg(argName, param));

                return this;
            }

            public struct ParameterArg
            {
                public string ArgName;
                public string ParameterName;

                public ParameterArg(string argName, NpgsqlParameter parameter)
                {
                    ArgName = argName;
                    ParameterName = parameter.ParameterName;
                }

                public string Declaration()
                {
                    return $"{ArgName} := :{ParameterName}";
                }
            }

        }

        public SprocCall Sproc(FunctionName function)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));

            var call = new SprocCall(this, function);
            _calls.Add(call);

            return call;
        }

        public void Delete(TableName table, object id, NpgsqlDbType dbType)
        {
            var param = addParameter(id, dbType);
            var call = new DeleteCall(table, param.ParameterName);
            _calls.Add(call);
        }


        public void DeleteWhere(TableName table, IWhereFragment @where)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (@where == null) throw new ArgumentNullException(nameof(@where));

            var whereClause = @where.ToSql(_command);
            var call = new DeleteWhereCall(table, whereClause);
            _calls.Add(call);
        }
    }
}