using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace DocumentDbTests.MultiTenancy;

public class MultiTenancyFixture: StoreFixture
{
    public MultiTenancyFixture(): base("multi_tenancy")
    {
        Options.Policies.AllDocumentsAreMultiTenanted();
        Options.Schema.For<User>().UseOptimisticConcurrency(true);
    }
}