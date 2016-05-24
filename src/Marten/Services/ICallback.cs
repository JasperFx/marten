using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services
{
    public interface ICallback
    {
        void Postprocess(DbDataReader reader, IList<Exception> exceptions);
        Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token);
    }
}