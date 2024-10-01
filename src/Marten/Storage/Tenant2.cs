using System;

namespace Marten.Storage;

public class Tenant
{
    public Tenant(string tenantId, IMartenDatabase inner)
    {
        Database = inner;
        TenantId = tenantId;
    }

    public string TenantId { get; }

    public IMartenDatabase Database { get; }

    public static Tenant ForDatabase(IMartenDatabase database)
    {
        return new Tenant(Tenancy.DefaultTenantId, database);
    }

    protected bool Equals(Tenant other)
    {
        return TenantId == other.TenantId && Equals(Database, other.Database);
    }

    public override bool Equals(object obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((Tenant)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TenantId, Database.Identifier);
    }
}
