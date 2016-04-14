using System;
using System.Data.Common;
using Marten.Schema;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.Results
{
    public class AnyQueryHandler<T> : IQueryHandler<bool>
    {
        private readonly DocumentQuery _query;

        public AnyQueryHandler(DocumentQuery query)
        {
            _query = query;
        }

        public Type SourceType => typeof (T);

        public void ConfigureCommand(NpgsqlCommand command)
        {
            _query.ConfigureForAny(command);
        }

        public bool Handle(DbDataReader reader, IIdentityMap map)
        {
            reader.Read();

            return reader.GetBoolean(0);
        }
    }
}