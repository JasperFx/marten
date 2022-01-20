using Marten.Events;

namespace Marten.Storage
{
    internal static class TenantExtensions
    {
        internal static IEventStorage EventStorage(this Tenant tenant)
        {
            return (IEventStorage)tenant.Database.StorageFor<IEvent>();
        }
    }
}
