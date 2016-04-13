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
        private readonly DocumentQuery _query;
        private ISelector<T> _selector = null;

        public ListQueryHandler(DocumentQuery query)
        {
            _query = query;
        }

        public Type SourceType => _query.SourceDocumentType;
        public void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command)
        {
            _selector = _query.ConfigureCommand<T>(schema, command);
        }

        public IList<T> Handle(DbDataReader reader, IIdentityMap map)
        {
            if (_selector == null)
            {
                throw new InvalidOperationException($"{nameof(ConfigureCommand)} needs to be called before {nameof(Handle)}");
            }

            return _selector.Read(reader, map);
        }
    }
}