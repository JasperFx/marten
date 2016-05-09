using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public abstract class ListQueryHandler<T> : IQueryHandler<IList<T>>
    {
        private readonly ISelector<T> _selector;

        public ListQueryHandler(ISelector<T> selector)
        {
            _selector = selector;
        }

        public IList<T> Handle(DbDataReader reader, IIdentityMap map)
        {
            return _selector.Read(reader, map);
        }

        public Task<IList<T>> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return _selector.ReadAsync(reader, map, token);
        }

        public ISelector<T> Selector => _selector;

        public abstract void ConfigureCommand(NpgsqlCommand command);
        public abstract Type SourceType { get; }
    }
}