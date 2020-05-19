using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;

namespace Marten.Linq.QueryHandlers
{
    public class UserSuppliedQueryHandler<T>: IQueryHandler<IReadOnlyList<T>>
    {
        private readonly object[] _parameters;
        private readonly ISelector<T> _selector;
        private readonly string _sql;
        private readonly DocumentStore _store;

        public UserSuppliedQueryHandler(DocumentStore store, string sql, object[] parameters)
        {
            _store = store;
            _sql = sql;
            _parameters = parameters;

            _selector = GetSelector();
        }

        public Type SourceType => typeof(T);

        public void ConfigureCommand(CommandBuilder builder)
        {
            if (!_sql.Contains("select", StringComparison.OrdinalIgnoreCase))
            {
                var mapping = _store.Storage.MappingFor(typeof(T)).ToQueryableDocument();
                var tableName = mapping.Table.QualifiedName;

                _selector.WriteSelectClause(builder, mapping);

                if (_sql.TrimStart().StartsWith("where", StringComparison.OrdinalIgnoreCase))
                    builder.Append(" ");
                else if (!_sql.Contains(" where ", StringComparison.OrdinalIgnoreCase)) builder.Append(" where ");
            }

            builder.Append(_sql);

            var firstParameter = _parameters.FirstOrDefault();

            if (_parameters.Length == 1 && firstParameter != null && firstParameter.IsAnonymousType())
                builder.AddParameters(firstParameter);
            else
                _parameters.Each(x =>
                {
                    var param = builder.AddParameter(x);
                    builder.UseParameter(param);
                });
        }

        public IReadOnlyList<T> Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return _selector.Read(reader, map, stats);
        }

        public Task<IReadOnlyList<T>> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats,
            CancellationToken token)
        {
            return _selector.ReadAsync(reader, map, stats, token);
        }


        private ISelector<T> GetSelector()
        {
            if (typeof(T).IsSimple())
                return new DeserializeSelector<T>(_store.Serializer);

            var mapping = _store.Tenancy.Default.MappingFor(typeof(T)).As<DocumentMapping>();

            return !mapping.IsHierarchy()
                ? (ISelector<T>)new DeserializeSelector<T>(_store.Serializer)
                : new WholeDocumentSelector<T>(mapping, _store.Tenancy.Default.StorageFor<T>());
        }
    }
}
