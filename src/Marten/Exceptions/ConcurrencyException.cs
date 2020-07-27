using System;
using System.Runtime.Serialization;

namespace Marten.Exceptions
{
    public class ConcurrencyException: Exception
    {
        public string DocType { get; set; }
        public object Id { get; set; }

        public ConcurrencyException(Type docType, object id) : base($"Optimistic concurrency check failed for {docType.FullName} #{id}")
        {
            DocType = docType.FullName;
            Id = id;
        }

        public ConcurrencyException(string message, Type docType, object id) : base(message)
        {
            DocType = docType?.FullName;
            Id = id;
        }


        protected ConcurrencyException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
