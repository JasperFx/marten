using Marten;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests;

public class duplicate_document_type_alias_detection : OneOffConfigurationsContext
{
    [Fact]
    public void throw_ambigous_alias_exception_when_you_have_duplicate_document_aliases()
    {
        theStore.Options.Providers.StorageFor<User>().ShouldNotBeNull();

        Exception<AmbiguousDocumentTypeAliasesException>.ShouldBeThrownBy(() =>
        {
            theStore.Options.Providers.StorageFor<User2>().ShouldNotBeNull();
        });
    }

    [DocumentAlias("user")]
    public class User2
    {
        public int Id { get; set; }
    }
}
