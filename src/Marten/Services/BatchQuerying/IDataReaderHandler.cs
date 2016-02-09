using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public interface IDataReaderHandler
    {
        Task Handle(DbDataReader reader, CancellationToken token);
    }

    public interface IDataReaderHandler<T> : IDataReaderHandler
    {
        void Configure(NpgsqlCommand command, DocumentQuery query);
        Task<T> ReturnValue { get; }

    }
}