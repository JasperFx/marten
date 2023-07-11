using Marten.Testing.Harness;
using Shouldly;
using System;
using System.Linq;
using Marten.Exceptions;
using Marten.Schema;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2327_using_identity_extension_on_class_with_invalid_type_id_field : IntegrationContext
{
    [Fact]
    public void can_override_the_identity_member_when_there_is_id_field_with_invalid_type()
    {
        Action action = () => StoreOptions(storeOptions =>
            storeOptions.Schema.For<OverriddenIdWithInvalidTypeIdField>().Identity(x => x.DocumentId));
        action.ShouldNotThrow();
    }

    [Fact]
    public void should_not_set_id_member_property_when_new_value_has_invalid_type()
    {
        Should.Throw<InvalidDocumentException>(() =>
        {
            var mapping = DocumentMapping.For<OverriddenIdWithInvalidTypeIdField>();
        });

    }

    public class OverriddenIdWithInvalidTypeIdField
    {
        public short Id { get; set; }

        public Guid DocumentId { get; set; }
    }

    public Bug_2327_using_identity_extension_on_class_with_invalid_type_id_field(DefaultStoreFixture fixture)
        : base(fixture) { }
}
