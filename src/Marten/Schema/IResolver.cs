using System.Data.Common;
using Marten.Services;

namespace Marten.Schema
{
    public interface IResolver<T>
    {
        T Resolve(DbDataReader reader, IIdentityMap map);
    }
}