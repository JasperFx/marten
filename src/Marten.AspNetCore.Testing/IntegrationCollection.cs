using Xunit;

namespace Marten.AspNetCore.Testing;

#region sample_integration_collection
[CollectionDefinition("integration")]
public class IntegrationCollection : ICollectionFixture<AppFixture>
{
}
#endregion
