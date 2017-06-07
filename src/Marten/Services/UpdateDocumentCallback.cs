using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services
{
    public class UpdateDocumentCallback<T> : ICallback
    {
        public object Id { get; }

        public UpdateDocumentCallback(object id)
        {
            Id = id;
        }

        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            if (!reader.Read() || reader.IsDBNull(0))
            {
                throw new NonExistentDocumentException(typeof(T), Id);
            };

            
        }

        public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new NonExistentDocumentException(typeof(T), Id);
            };

            var isNull = await reader.IsDBNullAsync(0, token).ConfigureAwait(false);
            if (isNull)
            {
                throw new NonExistentDocumentException(typeof(T), Id);
            }
        }
    }

    public class NonExistentDocumentException : Exception
    {
        public Type DocType { get; }
        public object Id { get; }

        public NonExistentDocumentException(Type docType, object id) : base((string)$"Nonexistent document {docType.FullName}: {id}")
        {
            DocType = docType;
            Id = id;
        }
    }
}