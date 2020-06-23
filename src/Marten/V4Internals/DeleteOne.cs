using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Util;

namespace Marten.V4Internals
{


    public abstract class DeleteOne<T, TId>: IStorageOperation
    {
        private readonly TId _id;

        public DeleteOne(TId id)
        {
            _id = id;
        }

        public abstract void ConfigureCommand(CommandBuilder builder, IMartenSession session);

        public Type DocumentType => typeof(T);

        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            // Nothing yet
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            // Nothing yet
            return Task.CompletedTask;
        }

        public StorageRole Role()
        {
            return StorageRole.Deletion;
        }

    }
}
