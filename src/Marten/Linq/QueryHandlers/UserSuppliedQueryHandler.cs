using System;
using System.Collections.Generic;
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

namespace Marten.Linq.QueryHandlers
{
    public class UserSuppliedQueryHandler<T> : IQueryHandler<IList<T>>
    {
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly string _sql;
        private readonly object[] _parameters;

        public UserSuppliedQueryHandler(IDocumentSchema schema, ISerializer serializer, string sql, object[] parameters)
        {
            _schema = schema;
            _serializer = serializer;
            _sql = sql;
            _parameters = parameters;
        }

        public Type SourceType => typeof(T);

        public void ConfigureCommand(CommandBuilder builder)
        {
            if (!_sql.Contains("select", StringComparison.OrdinalIgnoreCase))
            {
                var mapping = _schema.MappingFor(typeof(T)).ToQueryableDocument();
                var tableName = mapping.Table.QualifiedName;

                builder.Append("select data from ");
                builder.Append(tableName);

                if (_sql.StartsWith("where"))
                {
                    builder.Append(" ");
                }
                else if (!_sql.Contains(" where "))
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

        public IList<T> Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var selector = new DeserializeSelector<T>(_serializer);
            return selector.Read(reader, map, stats);
        }

        public Task<IList<T>> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var selector = new DeserializeSelector<T>(_serializer);

            return selector.ReadAsync(reader, map, stats, token);
        }

        
    }
}