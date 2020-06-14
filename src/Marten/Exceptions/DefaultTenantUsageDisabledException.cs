using System;
using System.Runtime.Serialization;
using Marten.Storage;

namespace Marten.Exceptions
{
    public class DefaultTenantUsageDisabledException : Exception
    {
        public DefaultTenantUsageDisabledException()
            : base($"Default tenant {Tenancy.DefaultTenantId} usage is disabled. Ensure to create a session by explicitly passing a non-default tenant in the method arg or SessionOptions.")
        {
        }

        protected DefaultTenantUsageDisabledException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
