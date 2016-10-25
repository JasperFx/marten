using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Model;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public class ListQueryHandler<T> : IQueryHandler<IList<T>>
    {
        private readonly LinqQuery<T> _query;

        public ListQueryHandler(LinqQuery<T> query)
        {
            _query = query;
        }

        public IList<T> Handle(DbDataReader reader, IIdentityMap map)
        {
            return _query.Selector.Read(reader, map);
        }

        public Task<IList<T>> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return _query.Selector.ReadAsync(reader, map, token);
        }


        public void ConfigureCommand(NpgsqlCommand command)
        {
            _query.ConfigureCommand(command);
        }

        public Type SourceType => _query.SourceType;
    }
}