using System;
using LamarCodeGeneration;
using Marten.Internal.Storage;

namespace Marten.Exceptions
{
    public class DocumentIdTypeMismatchException : Exception
    {
        public DocumentIdTypeMismatchException(IDocumentStorage storage, Type actualIdType) : base($"Id/Document type mismatch. The id type for the included document type {storage.SourceType.FullNameInCode()} is {storage.IdType.FullNameInCode()}, but {actualIdType.NameInCode()} was used.")
        {
        }

        public DocumentIdTypeMismatchException(Type documentType, Type idType): base(
            $"Invalid id of type {idType.NameInCode()} for document type {documentType.FullNameInCode()}")
        {

        }

        public DocumentIdTypeMismatchException(string message) : base(message)
        {
        }
    }
}
