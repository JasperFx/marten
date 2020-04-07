using System;
using Marten.Storage;

namespace Marten
{
    public class DefaultTenantUsageDisabledException : Exception
    {
        public DefaultTenantUsageDisabledException()
            : base($"Default tenant {Tenancy.DefaultTenantId} usage is disabled. Ensure to create a session by explicitly passing a non-default tenant in the method arg or SessionOptions.")
        {
        }
    }
}
