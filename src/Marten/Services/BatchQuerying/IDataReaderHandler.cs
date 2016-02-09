using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public interface IDataReaderHandler
    {
        Task Handle(DbDataReader reader, CancellationToken token);
    }
}