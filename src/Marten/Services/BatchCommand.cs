using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Baseline;
using Marten.Linq;
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
            private readonly string _table;
            private readonly string _whereClause;

            public DeleteWhereCall(string table, string whereClause)
            {
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

        public class UpdateWhereCall : ICall
        {            
            private readonly SprocCall _updateCall;
            private string _whereClause;

            public UpdateWhereCall(SprocCall updateCall, string whereClause)
            {                
                _updateCall = updateCall;
                _whereClause = whereClause;
            }

            public void WriteToSql(StringBuilder builder)
            {
                _updateCall.WriteToSql(builder);
                builder.Append($"where exists (select 1 {_whereClause}");
            }
        }

        public class SprocCall : ICall
        {
            private readonly BatchCommand _parent;
            private readonly string _sprocName;
            private readonly IList<ParameterArg> _parameters = new List<ParameterArg>();
            private readonly Lazy<IList<Tuple<string, NpgsqlParameterCollection>>> _postConditions = new Lazy<IList<Tuple<string, NpgsqlParameterCollection>>>(() => new List<Tuple<string, NpgsqlParameterCollection>>());

            public SprocCall(BatchCommand parent, string sprocName)
            {
                _parent = parent;
                _sprocName = sprocName;
            }

            public void WriteToSql(StringBuilder builder)
            {
                var parameters = _parameters.Select(x => x.Declaration()).Join(", ");
                builder.AppendFormat("select {0}({1})", _sprocName, parameters);

                if (_postConditions.IsValueCreated)
                {                    
                    _postConditions.Value.Each(x =>
                    {
                        var query = x.Item1;
                        x.Item2?.Each(p =>
                        {
                            var parameter = _parent.addParameter(p.Value, p.NpgsqlDbType);
                            query = query.Replace($":{p.ParameterName}", $":{parameter.ParameterName}");
                        });
                        builder.AppendFormat($"{query}");
                    });
                }
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

            public SprocCall PostCondition(string @where, NpgsqlParameterCollection parameters)
            {
                _postConditions.Value.Add(Tuple.Create(@where, parameters));

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


        public void DeleteWhere(string tableName, IWhereFragment @where)
        {
            var whereClause = @where.ToSql(_command);
            var call = new DeleteWhereCall(tableName, whereClause);
            _calls.Add(call);
        }
    }
}