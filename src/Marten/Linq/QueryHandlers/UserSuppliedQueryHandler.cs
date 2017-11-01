using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Marten.Util;

namespace Marten.Linq.QueryHandlers
{
    public class UserSuppliedQueryHandler<T> : IQueryHandler<IReadOnlyList<T>>
    {
        private readonly DocumentStore _store;
        private readonly string _sql;
        private readonly object[] _parameters;

        public UserSuppliedQueryHandler(DocumentStore store, string sql, object[] parameters)
        {
            _store = store;
            _sql = sql;
            _parameters = parameters;
        }

        public Type SourceType => typeof(T);

        public void ConfigureCommand(CommandBuilder builder)
        {
            if (!_sql.Contains("select", StringComparison.OrdinalIgnoreCase))
            {
                var mapping = _store.Storage.MappingFor(typeof(T)).ToQueryableDocument();
                var tableName = mapping.Table.QualifiedName;

                builder.Append("select data from ");
                builder.Append(tableName);

                if (_sql.StartsWith("where", StringComparison.OrdinalIgnoreCase))
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

        public IReadOnlyList<T> Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var selector = new DeserializeSelector<T>(_store.Serializer);
            return selector.Read(reader, map, stats);
        }

        public Task<IReadOnlyList<T>> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var selector = new DeserializeSelector<T>(_store.Serializer);

            return selector.ReadAsync(reader, map, stats, token);
        }

        
    }
}