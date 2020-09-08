using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq.Includes
{
    public interface IIncludeReader
    {
        void Read(DbDataReader reader);
        Task ReadAsync(DbDataReader reader, CancellationToken token);
    }
}
