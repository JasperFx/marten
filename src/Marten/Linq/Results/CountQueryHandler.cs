using System;
using System.Data.Common;
using Marten.Schema;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.Results
{
    public class CountQueryHandler<T> : IQueryHandler<long>
    {
        private readonly DocumentQuery _query;

        public CountQueryHandler(DocumentQuery query)
        {
            _query = query;
        }

        public Type SourceType => _query.SourceDocumentType;

        public void ConfigureCommand(NpgsqlCommand command)
        {
            _query.ConfigureForCount(command);
        }

        public long Handle(DbDataReader reader, IIdentityMap map)
        {
            var hasNext = reader.Read();
            return hasNext ? reader.GetInt64(0) : 0;
        }
    }
}