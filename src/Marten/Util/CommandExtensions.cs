using System;
using Npgsql;

namespace Marten.Util
{
    public static class CommandExtensions
    {
        public static NpgsqlParameter AddParameter(this NpgsqlCommand command, object value)
        {
            var name = "arg" + command.Parameters.Count;

            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);

            return parameter;
        }
    }
}