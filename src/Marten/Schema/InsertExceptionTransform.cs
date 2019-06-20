using System;
using Marten.Services;

namespace Marten.Schema
{
    public sealed class InsertExceptionTransform<T>: IExceptionTransform
    {
        private readonly object id;
        private readonly string tableName;

        private const string ExpectedMessage = "23505: duplicate key value violates unique constraint";

        public InsertExceptionTransform(object id, string tableName)
        {
            this.id = id;
            this.tableName = tableName;
        }

        public bool TryTransform(Exception original, out Exception transformed)
        {
            transformed = null;

            if (original.Message?.IndexOf(ExpectedMessage, StringComparison.OrdinalIgnoreCase) > -1 &&
                original.Message?.IndexOf(tableName, StringComparison.Ordinal) > -1)
            {
                transformed = new DocumentAlreadyExistsException(original, typeof(T), id);
                return true;
            }
            return false;
        }
    }

    public sealed class DocumentAlreadyExistsException: Exception
    {
        public Type DocType { get; }
        public object Id { get; }

        public DocumentAlreadyExistsException(Exception inner, Type docType, object id) : base((string)$"Document already exists {docType.FullName}: {id}", inner)
        {
            DocType = docType;
            Id = id;
        }
    }
}
