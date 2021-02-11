using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Baseline;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Schema.Arguments;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Util
{
    public static class CommandExtensions
    {
        public static NpgsqlCommand BuildCommand(this IMartenSession session, Statement statement)
        {
            var command = new NpgsqlCommand();
            var builder = new CommandBuilder(command);

            statement.Configure(builder);

            command.CommandText = builder.ToString();

            var tenantParameter = command.Parameters.FirstOrDefault(x => x.ParameterName == TenantIdArgument.ArgName);

            if (tenantParameter != null)
            {
                tenantParameter.Value = session.Tenant.TenantId;
            }

            return command;
        }

        public static NpgsqlCommand BuildCommand(this IMartenSession session, IQueryHandler handler)
        {
            var command = new NpgsqlCommand();
            var builder = new CommandBuilder(command);

            handler.ConfigureCommand(builder, session);

            command.CommandText = builder.ToString();

            var tenantParameter = command.Parameters.FirstOrDefault(x => x.ParameterName == TenantIdArgument.ArgName);

            if (tenantParameter != null)
            {
                tenantParameter.Value = session.Tenant.TenantId;
            }

            return command;
        }

        public static NpgsqlCommand BuildCommand(this IMartenSession session, IEnumerable<IQueryHandler> handlers)
        {
            var command = new NpgsqlCommand();
            var builder = new CommandBuilder(command);

            foreach (var handler in handlers)
            {
                handler.ConfigureCommand(builder, session);
                builder.Append(";");
            }

            command.CommandText = builder.ToString();

            var tenantParameter = command.Parameters.FirstOrDefault(x => x.ParameterName == TenantIdArgument.ArgName);

            if (tenantParameter != null)
            {
                tenantParameter.Value = session.Tenant.TenantId;
            }

            return command;
        }

        public static int RunSql(this NpgsqlConnection conn, params string[] sqls)
        {
            var sql = sqls.Join(";");
            return conn.CreateCommand().Sql(sql).ExecuteNonQuery();
        }

        public static IEnumerable<T> Fetch<T>(this NpgsqlCommand cmd, string sql, Func<DbDataReader, T> transform, params object[] parameters)
        {
            cmd.WithText(sql);
            parameters.Each(x =>
            {
                var param = cmd.AddParameter(x);
                cmd.CommandText = cmd.CommandText.UseParameter(param);
            });

            var list = new List<T>();

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    list.Add(transform(reader));
                }
            }

            return list;
        }

        public static void AddParameters(this NpgsqlCommand command, object parameters)
        {
            if (parameters == null)
                return;

            var parameterDictionary = parameters.GetType().GetProperties().ToDictionary(x => x.Name, x => x.GetValue(parameters, null));

            foreach (var item in parameterDictionary)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = item.Key;
                parameter.Value = item.Value ?? DBNull.Value;

                command.Parameters.Add(parameter);
            }
        }

        public static NpgsqlParameter AddParameter(this NpgsqlCommand command, object value, NpgsqlDbType? dbType = null)
        {
            var name = "p" + command.Parameters.Count;

            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;

            if (dbType.HasValue)
            {
                parameter.NpgsqlDbType = dbType.Value;
            }

            command.Parameters.Add(parameter);

            return parameter;
        }

        public static NpgsqlParameter AddNamedParameter(this NpgsqlCommand command, string name, object value)
        {
            var existing = command.Parameters.FirstOrDefault(x => x.ParameterName == name);
            if (existing != null) return existing;

            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);

            return parameter;
        }

        public static NpgsqlCommand With(this NpgsqlCommand command, string name, Guid value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.NpgsqlDbType = NpgsqlDbType.Uuid;
            parameter.Value = value;

            command.Parameters.Add(parameter);

            return command;
        }

        public static NpgsqlCommand With(this NpgsqlCommand command, string name, string value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.NpgsqlDbType = NpgsqlDbType.Varchar;

            if (value == null)
            {
                parameter.Value = DBNull.Value;
            }
            else
            {
                parameter.Value = value;
            }

            command.Parameters.Add(parameter);

            return command;
        }

        public static NpgsqlCommand With(this NpgsqlCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);

            return command;
        }

        public static NpgsqlCommand With(this NpgsqlCommand command, string name, object value, NpgsqlDbType dbType)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            parameter.NpgsqlDbType = dbType;
            command.Parameters.Add(parameter);

            return command;
        }

        public static NpgsqlCommand WithJsonParameter(this NpgsqlCommand command, string name, string json)
        {
            command.Parameters.Add(name, NpgsqlDbType.Jsonb).Value = json;

            return command;
        }

        public static NpgsqlCommand Sql(this NpgsqlCommand cmd, string sql)
        {
            cmd.CommandText = sql;
            return cmd;
        }

        public static NpgsqlCommand CallsSproc(this NpgsqlCommand cmd, DbObjectName function)
        {
            if (cmd == null)
                throw new ArgumentNullException(nameof(cmd));
            if (function == null)
                throw new ArgumentNullException(nameof(function));

            cmd.CommandText = function.QualifiedName;
            cmd.CommandType = CommandType.StoredProcedure;

            return cmd;
        }

        public static NpgsqlCommand Returns(this NpgsqlCommand command, string name, NpgsqlDbType type)
        {
            var parameter = command.AddParameter(name);
            parameter.NpgsqlDbType = type;
            parameter.Direction = ParameterDirection.ReturnValue;
            return command;
        }

        public static NpgsqlCommand WithText(this NpgsqlCommand command, string sql)
        {
            command.CommandText = sql;
            return command;
        }

        public static NpgsqlCommand CreateCommand(this NpgsqlConnection conn, string command)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = command;

            return cmd;
        }
    }
}
