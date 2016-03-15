using System.Data.Common;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public interface ISelector<T>
    {
        T Resolve(DbDataReader reader, IIdentityMap map);

        string[] SelectFields(IDocumentMapping mapping);
    }
}