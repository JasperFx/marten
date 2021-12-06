using Marten.Schema;
using Weasel.Postgresql;

namespace Marten.Storage
{
    internal abstract class Tenancy
    {
        public const string DefaultTenantId = "*DEFAULT*";

        protected Tenancy(StoreOptions options)
        {
            Options = options;
        }

        internal StoreOptions Options { get; }


    }
}
