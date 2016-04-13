using System;
using System.ComponentModel;
using System.Data.Common;
using Marten.Schema;
using Marten.Services;
using Marten.Services.BatchQuerying;
using Npgsql;

namespace Marten.Linq.Results
{
    public interface IQueryHandler<T>
    {
        Type SourceType { get; }

        // It's done this way so that the same query handler can swing back
        // and forth between batched queries and standalone queries
        void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command);

        // Sync
        T Handle(DbDataReader reader, IIdentityMap map);

        // Async
        //Task<T> HandleAsync(DbDataReader reader, IIdentityMap map, CancellationToken token);
    }

    /*
    X Any
    X Count, LongCount
    X ToList
    X Single
    X SingleOrDefault
    X First
    X FirstOrDefault
    Aggregate functions

    */
}