using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq.Model;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public class AggregateQueryHandler<T> : IQueryHandler<T>
    {
        private readonly string _operator;
        private readonly ILinqQuery _query;
        private readonly ISelector<T> _selector;

        public AggregateQueryHandler(string @operator, ILinqQuery query)
        {
            _operator = @operator;
            _query = query;


            if (typeof(T).Closes(typeof(Nullable<>)))
            {
                var typeInfo = typeof(T).GetTypeInfo();
                var arguments = typeInfo.IsGenericTypeDefinition
                    ? typeInfo.GenericTypeParameters
                    : typeInfo.GenericTypeArguments;
                var inner = arguments.First();
                _selector = typeof(NullableScalarSelector<>).CloseAndBuildAs<ISelector<T>>(inner);
            }
            else
            {
                _selector = new ScalarSelector<T>();
            }
        }

        public Type SourceType => _query.SourceType;

        public void ConfigureCommand(NpgsqlCommand command)
        {
            _query.ConfigureAggregate(command, _operator);
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

        public static AggregateQueryHandler<T> Min(ILinqQuery query)
        {
            return new AggregateQueryHandler<T>("min({0})", query);
        }

        public static AggregateQueryHandler<T> Max(ILinqQuery query)
        {
            return new AggregateQueryHandler<T>("max({0})", query);
        }

        public static AggregateQueryHandler<T> Sum(ILinqQuery query)
        {
            return new AggregateQueryHandler<T>("coalesce(sum({0}),0)", query);
        }

        public static AggregateQueryHandler<T> Average(ILinqQuery query)
        {
            return new AggregateQueryHandler<T>("avg({0})", query);
        }
    }
}