using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    [Obsolete("Remove as part of GUT")]
    public interface IDataReaderHandler
    {
        Task Handle(DbDataReader reader, CancellationToken token);
    }



    [Obsolete("Remove as part of GUT")]
    public interface IDataReaderHandler<T> : IDataReaderHandler
    {

        // TODO -- move this up to IDataReaderHandler
        void Configure(NpgsqlCommand command, DocumentQuery query);
        Task<T> ReturnValue { get; }

    }
}