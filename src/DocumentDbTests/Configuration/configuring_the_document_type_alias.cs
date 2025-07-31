using JasperFx.Core.Reflection;
using Marten;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Configuration;

public class configuring_the_document_type_alias
{
    [Fact]
    public void DocumentAlias_attribute_changes_the_alias()
    {
        var mapping = DocumentMapping.For<Tractor>();

        mapping.Alias.ShouldBe("johndeere");
        mapping.TableName.Name.ShouldBe("mt_doc_johndeere");
    }

    [Fact]
    public void document_alias_can_be_overridden_with_the_marten_registry()
    {
        #region sample_marten-registry-to-override-document-alias
        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);

            _.Schema.For<User>().DocumentAlias("folks");
        });
        #endregion

        store.StorageFeatures.MappingFor(typeof(User)).As<DocumentMapping>().Alias.ShouldBe("folks");
    }

    #region sample_using-document-alias-attribute
    [DocumentAlias("johndeere")]
    public class Tractor
    {
        public string id;
    }
    #endregion
}
