using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;

namespace Marten.Internal.Operations
{
    public interface IStorageOperation : IQueryHandler
    {
        Type DocumentType { get; }

        void Postprocess(DbDataReader reader, IList<Exception> exceptions);

        Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token);

        OperationRole Role();
    }
}
