using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Model;
using Marten.Services;
using Marten.Util;

namespace Marten.Linq.QueryHandlers
{
    public class ListQueryHandler<T> : IQueryHandler<IReadOnlyList<T>>
    {
        private readonly LinqQuery<T> _query;

        public ListQueryHandler(LinqQuery<T> query)
        {
            _query = query;
        }

        public IReadOnlyList<T> Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return _query.Selector.Read(reader, map, stats);
        }

        public Task<IReadOnlyList<T>> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return _query.Selector.ReadAsync(reader, map, stats, token);
        }


        public void ConfigureCommand(CommandBuilder builder)
        {
            _query.ConfigureCommand(builder);
        }

        public Type SourceType => _query.SourceType;
    }
}