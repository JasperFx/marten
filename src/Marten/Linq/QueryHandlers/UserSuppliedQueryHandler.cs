using System;
using System.Collections.Generic;
using System.Data.Common;
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

        public void ConfigureCommand(NpgsqlCommand command)
        {
            var sql = _sql;
            if (!sql.Contains("select", StringComparison.OrdinalIgnoreCase))
            {
                var mapping = _schema.MappingFor(typeof(T)).ToQueryableDocument();
                var tableName = mapping.Table.QualifiedName;
                sql = "select data from {0} {1}".ToFormat(tableName, sql);
            }

            _parameters.Each(x =>
            {
                var param = command.AddParameter(x);
                sql = sql.UseParameter(param);
            });

            command.AppendQuery(sql);
        }

        public IList<T> Handle(DbDataReader reader, IIdentityMap map)
        {
            var selector = new DeserializeSelector<T>(_serializer);
            return selector.Read(reader, map);
        }

        public Task<IList<T>> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var selector = new DeserializeSelector<T>(_serializer);

            return selector.ReadAsync(reader, map, token);
        }
    }
}