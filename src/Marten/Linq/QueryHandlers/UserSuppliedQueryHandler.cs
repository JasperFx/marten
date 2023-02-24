using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Linq.QueryHandlers;

internal class UserSuppliedQueryHandler<T>: IQueryHandler<IReadOnlyList<T>>
{
    private readonly object[] _parameters;
    private readonly ISelectClause _selectClause;
    private readonly ISelector<T> _selector;
    private readonly string _sql;

    public UserSuppliedQueryHandler(IMartenSession session, string sql, object[] parameters)
    {
        _sql = sql.TrimStart();
        _parameters = parameters;
        SqlContainsCustomSelect = _sql.StartsWith("select", StringComparison.OrdinalIgnoreCase);

        _selectClause = GetSelectClause(session);
        _selector = (ISelector<T>)_selectClause.BuildSelector(session);
    }

    public bool SqlContainsCustomSelect { get; }

    public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
    {
        if (!SqlContainsCustomSelect)
        {
            _selectClause.WriteSelectClause(builder);

            if (_sql.StartsWith("where", StringComparison.OrdinalIgnoreCase) || _sql.StartsWith("order", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" ");
            }
            else if (!_sql.Contains(" where ", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" where ");
            }
        }


        var firstParameter = _parameters.FirstOrDefault();

        if (_parameters.Length == 1 && firstParameter != null && firstParameter.IsAnonymousType())
        {
            builder.Append(_sql);
            builder.AddParameters(firstParameter);
        }
        else
        {
            var cmdParameters = builder.AppendWithParameters(_sql);
            if (cmdParameters.Length != _parameters.Length)
            {
                throw new InvalidOperationException("Wrong number of supplied parameters");
            }

            for (var i = 0; i < cmdParameters.Length; i++)
            {
                if (_parameters[i] == null)
                {
                    cmdParameters[i].Value = DBNull.Value;
                }
                else
                {
                    cmdParameters[i].Value = _parameters[i];
                    cmdParameters[i].NpgsqlDbType =
                        PostgresqlProvider.Instance.ToParameterType(_parameters[i].GetType());
                }
            }
        }
    }

    public IReadOnlyList<T> Handle(DbDataReader reader, IMartenSession session)
    {
        var list = new List<T>();

        while (reader.Read())
        {
            var item = _selector.Resolve(reader);
            list.Add(item);
        }

        return list;
    }

    public async Task<IReadOnlyList<T>> HandleAsync(DbDataReader reader, IMartenSession session,
        CancellationToken token)
    {
        var list = new List<T>();

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var item = await _selector.ResolveAsync(reader, token).ConfigureAwait(false);
            list.Add(item);
        }

        return list;
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        return reader.As<NpgsqlDataReader>().StreamMany(stream, token);
    }


    private ISelectClause GetSelectClause(IMartenSession session)
    {
        if (typeof(T) == typeof(string))
        {
            return new ScalarStringSelectClause("", "");
        }

        if (typeof(T).IsSimple())
        {
            return typeof(ScalarSelectClause<>).CloseAndBuildAs<ISelectClause>("", "", typeof(T));
        }

        if (SqlContainsCustomSelect)
        {
            return new DataSelectClause<T>("", "");
        }

        return session.StorageFor(typeof(T));
    }
}
