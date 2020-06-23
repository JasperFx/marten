using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.V4Internals
{
    public interface ISelector
    {

    }

    public interface ISelector<T> : ISelector
    {
        T Resolve(DbDataReader reader);

        Task<T> ResolveAsync(DbDataReader reader, CancellationToken token);
    }



}
