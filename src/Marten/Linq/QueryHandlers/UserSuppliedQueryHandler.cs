using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal;
using Marten.Internal.Linq;
using Marten.Util;

namespace Marten.Linq.QueryHandlers
{
    public class UserSuppliedQueryHandler<T>: IQueryHandler<IReadOnlyList<T>>
    {
        private readonly object[] _parameters;
        private readonly ISelector<T> _selector;
        private readonly string _sql;
        private readonly bool _sqlContainsCustomSelect;
        private readonly ISelectClause _selectClause;

        public UserSuppliedQueryHandler(IMartenSession session, string sql, object[] parameters)
        {
            _sql = sql;
            _parameters = parameters;
            _sqlContainsCustomSelect = _sql.Contains("select", StringComparison.OrdinalIgnoreCase);

            _selectClause = GetSelectClause(session);
            _selector = (ISelector<T>) _selectClause.BuildSelector(session);
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            if (!_sqlContainsCustomSelect)
            {
                _selectClause.WriteSelectClause(builder);

                if (_sql.TrimStart().StartsWith("where", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append(" ");
                }
                else if (!_sql.Contains(" where ", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append(" where ");
                }
            }

            builder.Append(_sql);

            var firstParameter = _parameters.FirstOrDefault();

            if (_parameters.Length == 1 && firstParameter != null && firstParameter.IsAnonymousType())
            {
                builder.AddParameters(firstParameter);
            }
            else
            {
                _parameters.Each(x =>
                {
                    var param = builder.AddParameter(x);
                    builder.UseParameter(param);
                });
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

            if (_sqlContainsCustomSelect)
            {
                return new DataSelectClause<T>("", "");
            }

            return session.StorageFor(typeof(T));
        }
    }
}
