using System;
using System.Data.Common;
using Marten.Schema;
using Marten.Services;
using Npgsql;

namespace Marten.Linq.Results
{
    public abstract class OnlyOneResultHandler<T> : IQueryHandler<T>
    {
        public DocumentQuery Query { get; set; }
        public ISelector<T> Selector { get; set; }

        public OnlyOneResultHandler(DocumentQuery query)
        {
            Query = query;
        }

        public Type SourceType => Query.SourceDocumentType;

        public void ConfigureCommand(IDocumentSchema schema, NpgsqlCommand command)
        {
            Selector = writeCommand(schema, command);
        }

        protected abstract ISelector<T> writeCommand(IDocumentSchema schema, NpgsqlCommand command);

        public T Handle(DbDataReader reader, IIdentityMap map)
        {
            if (Selector == null)
            {
                throw new InvalidOperationException($"{nameof(ConfigureCommand)} needs to be called before {nameof(Handle)}");
            }

            var hasResult = reader.Read();
            if (!hasResult) return defaultValue();

            var result = Selector.Resolve(reader, map);

            if (reader.Read()) assertMoreResults();

            return result;
        }

        protected virtual void assertMoreResults()
        {
            // nothing;
        }

        protected virtual T defaultValue()
        {
            return default(T);
        }
    }
}