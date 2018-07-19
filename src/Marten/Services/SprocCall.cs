using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Schema;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Services
{
    public class SprocCall : IStorageOperation
    {
        private readonly DbObjectName _function;
        private readonly IList<ParameterArg> _parameters = new List<ParameterArg>();
        private readonly BatchCommand _parent;

        public SprocCall(BatchCommand parent, DbObjectName function)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (function == null) throw new ArgumentNullException(nameof(function));

            _parent = parent;
            _function = function;
        }

        // TODO -- merge this into the upsert's
        public Type DocumentType { get; } = null;

        public void ConfigureCommand(CommandBuilder builder)
        {
            builder.Append("select ");
            builder.Append(_function.QualifiedName);
            builder.Append("(");

            if (_parameters.Any())
            {
                _parameters[0].AppendDeclaration(builder);

                for (var i = 1; i < _parameters.Count; i++)
                {
                    builder.Append(", ");
                    _parameters[i].AppendDeclaration(builder);
                }
            }

            builder.Append(")");
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

        public SprocCall JsonBodies(string argName, ArraySegment<char>[] bodies)
        {
            return Param(argName, bodies, NpgsqlDbType.Jsonb | NpgsqlDbType.Array);
        }

        public SprocCall Param(string argName, object value, NpgsqlDbType dbType)
        {
            if ((value is Enum || value is Guid) && dbType == NpgsqlDbType.Varchar)
                value = value.ToString();

            _parameters.Add(new ParameterArg(argName, value, dbType));

            return this;
        }

        public SprocCall Param(string argName, object value, NpgsqlDbType dbType, int size)
        {
            _parameters.Add(new ParameterArg(argName, value, dbType, size));

            return this;
        }

        public struct ParameterArg
        {
            private readonly string _argName;
            private readonly object _value;
            private readonly NpgsqlDbType _dbType;
            private readonly int _size;

            public ParameterArg(string argName, object value, NpgsqlDbType dbType, int size = 0)
            {
                _argName = argName;
                _value = value;
                _dbType = dbType;
                _size = size;
            }

            public void AppendDeclaration(CommandBuilder builder)
            {
                var parameter = builder.AddParameter(_value, _dbType);
                if (_size > 0)
                {
                    parameter.Size = _size;
                }

                builder.Append(_argName);
                builder.Append(" := :");
                builder.Append(parameter.ParameterName);
            }
        }
    }
}