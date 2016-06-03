using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq.QueryHandlers
{
    public class AggregateQueryHandler<T> : IQueryHandler<T>
    {
        private readonly string _operator;
        private readonly IDocumentSchema _schema;
        private readonly QueryModel _query;
        private ISelector<T> _selector;

        public static AggregateQueryHandler<T> Min(IDocumentSchema schema, QueryModel query)
        {
            return new AggregateQueryHandler<T>("min({0})", schema, query);
        }

        public static AggregateQueryHandler<T> Max(IDocumentSchema schema, QueryModel query)
        {
            return new AggregateQueryHandler<T>("max({0})", schema, query);
        }

        public static AggregateQueryHandler<T> Sum(IDocumentSchema schema, QueryModel query)
        {
            return new AggregateQueryHandler<T>("coalesce(sum({0}),0)", schema, query);
        }

        public static AggregateQueryHandler<T> Average(IDocumentSchema schema, QueryModel query)
        {
            return new AggregateQueryHandler<T>("avg({0})", schema, query);
        }

        public AggregateQueryHandler(string @operator, IDocumentSchema schema, QueryModel query)
        {
            _operator = @operator;
            _schema = schema;
            _query = query;


            if (typeof (T).Closes(typeof (Nullable<>)))
            {
                var typeInfo = typeof(T).GetTypeInfo();
                var arguments = typeInfo.IsGenericTypeDefinition
                    ? typeInfo.GenericTypeParameters
                    : typeInfo.GenericTypeArguments;
                var inner = arguments.First();
                _selector = typeof (NullableScalarSelector<>).CloseAndBuildAs<ISelector<T>>(inner);
            }
            else
            {
                _selector = new ScalarSelector<T>();
            }
        }

        public Type SourceType => _query.SourceType();

        public void ConfigureCommand(NpgsqlCommand command)
        {
            var mapping = _schema.MappingFor(_query.SourceType()).ToQueryableDocument();
            var locator = mapping.JsonLocator(_query.SelectClause.Selector);
            var field = _operator.ToFormat(locator);

            var sql = $"select {field} from {mapping.Table.QualifiedName} as d";

            var @where = _schema.BuildWhereFragment(mapping, _query);

            sql = sql.AppendWhere(@where, command);

            command.AppendQuery(sql);

        }

        public T Handle(DbDataReader reader, IIdentityMap map)
        {
            return reader.Read() ? _selector.Resolve(reader, map) : default(T);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var hasValue = await reader.ReadAsync(token).ConfigureAwait(false);

            return hasValue 
                ? await _selector.ResolveAsync(reader, map, token).ConfigureAwait(false) 
                : default(T);
        }
    }
}