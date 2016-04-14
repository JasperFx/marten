using System;
using System.Collections.Generic;
using System.Data.Common;
using Marten.Schema;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.Results
{
    public class ListQueryHandler<T> : IQueryHandler<IList<T>>
    {
        private readonly IDocumentSchema _schema;
        private readonly DocumentQuery _query;
        private readonly ISelector<T> _selector;

        public ListQueryHandler(IDocumentSchema schema, DocumentQuery query)
        {
            // TODO -- this is temporary until DocumentQuery starts to go away
            _selector = query.ConfigureCommand<T>(schema, new NpgsqlCommand());
            _schema = schema;
            _query = query;
        }

        public Type SourceType => _query.SourceDocumentType;
        public void ConfigureCommand(NpgsqlCommand command)
        {
            _query.ConfigureCommand<T>(_schema, command);
        }

        public IList<T> Handle(DbDataReader reader, IIdentityMap map)
        {
            return _selector.Read(reader, map);
        }
    }
}