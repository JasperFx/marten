using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq.Includes
{
    /// <summary>
    /// Used internally to process Include() operations
    /// in the Linq support
    /// </summary>
    public interface IIncludeReader
    {
        void Read(DbDataReader reader);
        Task ReadAsync(DbDataReader reader, CancellationToken token);
    }
}
