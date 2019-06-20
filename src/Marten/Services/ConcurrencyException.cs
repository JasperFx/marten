using System;

namespace Marten.Services
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
    }
}
