using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.V4Internals
{
    public interface IStorageOperation : IQueryHandler
    {
        Type DocumentType { get; }

        void Postprocess(DbDataReader reader, IList<Exception> exceptions);

        Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token);

        StorageRole Role();
    }
}
