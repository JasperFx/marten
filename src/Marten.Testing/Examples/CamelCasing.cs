using Marten.Linq;
using Marten.Linq.Parsing;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Examples;

public sealed class CamelCasing
{
    [Fact]
    public void SerializeToCamelCase()
    {
        #region sample_sample-serialize-to-camelcase
        var store = DocumentStore.For(storeOptions =>
        {
            // Change default casing to CamelCase
            storeOptions.UseDefaultSerialization(casing: Casing.CamelCase);
            #endregion
            storeOptions.Connection("");
        });

        var field = store.StorageFeatures.MappingFor(typeof(User))
            .QueryMembers.MemberFor<User>(x => x.FirstName);

        Assert.Equal(@"d.data ->> 'firstName'", field.TypedLocator);
    }
}
