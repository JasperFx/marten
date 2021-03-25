using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Services
{
    internal interface ISingleQueryHandler<T>
    {
        NpgsqlCommand BuildCommand();
        Task<T> HandleAsync(DbDataReader reader, CancellationToken token);


    }
}