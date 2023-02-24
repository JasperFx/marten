using Marten.Storage;

namespace Marten.Exceptions;

public class InvalidTenantForDatabaseException : MartenException
{
    public InvalidTenantForDatabaseException(string tenantId, IMartenDatabase database) : base($"Tenant Id '{tenantId}' is not stored in the current database '{database.Identifier}'")
    {
    }
}
