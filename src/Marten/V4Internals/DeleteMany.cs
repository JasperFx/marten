using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Util;

namespace Marten.V4Internals
{
    public abstract class DeleteMany<T>: IStorageOperation
    {
        private readonly IWhereFragment _where;

        public DeleteMany(IWhereFragment where)
        {
            _where = @where;
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
