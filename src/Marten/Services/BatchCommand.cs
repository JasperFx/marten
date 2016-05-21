using System;
using System.Collections.Generic;
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
        public ISerializer Serializer { get; }
        private int _counter = 0;
        private readonly NpgsqlCommand _command = new NpgsqlCommand();
        private readonly IList<ICall> _calls = new List<ICall>();

        public BatchCommand(ISerializer serializer)
        {
            Serializer = serializer;
        }

        public int Count => _calls.Count;

        public NpgsqlParameter AddParameter(object value, NpgsqlDbType dbType)
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

        public void AddCall(ICall call)
        {
            _calls.Add(call);
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
            var param = AddParameter(id, dbType);
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