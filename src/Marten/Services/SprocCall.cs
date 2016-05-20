using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Baseline;
using Marten.Schema;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Services
{
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

        public SprocCall Param(string argName, Guid[] values)
        {
            return Param(argName, values, NpgsqlDbType.Uuid | NpgsqlDbType.Array);
        }

        public SprocCall Param(string argName, string value)
        {
            return Param(argName, value, NpgsqlDbType.Varchar);
        }

        public SprocCall Param(string argName, string[] values)
        {
            return Param(argName, values, NpgsqlDbType.Varchar | NpgsqlDbType.Array);
        }

        public SprocCall JsonEntity(string argName, object value)
        {
            var json = _parent.Serializer.ToJson(value);
            return Param(argName, json, NpgsqlDbType.Jsonb);
        }

        public SprocCall JsonBody(string argName, string json)
        {
            return Param(argName, json, NpgsqlDbType.Jsonb);
        }

        public SprocCall JsonBodies(string argName, string[] bodies)
        {
            return Param(argName, bodies, NpgsqlDbType.Jsonb | NpgsqlDbType.Array);
        }

        public SprocCall Param(string argName, object value, NpgsqlDbType dbType)
        {
            var param = _parent.AddParameter(value, dbType);

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
}