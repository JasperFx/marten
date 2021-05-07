using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Baseline;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Schema.Arguments;
using Npgsql;
using NpgsqlTypes;
#nullable enable
namespace Marten.Util
{
    internal static class CommandExtensions
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


    }
}
