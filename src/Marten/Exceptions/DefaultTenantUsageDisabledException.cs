using System.Runtime.Serialization;
using JasperFx;
using Marten.Storage;

namespace Marten.Exceptions;

public class DefaultTenantUsageDisabledException: MartenException
{
    public DefaultTenantUsageDisabledException()
        : base(
            $"Default tenant {TenancyConstants.DefaultTenantId} usage is disabled. Ensure to create a session by explicitly passing a non-default tenant in the method arg or SessionOptions.")
    {
    }

    public DefaultTenantUsageDisabledException(string message): base(
        $"Default tenant {TenancyConstants.DefaultTenantId} usage is disabled. {message}")
    {
    }

    protected DefaultTenantUsageDisabledException(SerializationInfo info, StreamingContext context): base(info, context)
    {
    }
}
