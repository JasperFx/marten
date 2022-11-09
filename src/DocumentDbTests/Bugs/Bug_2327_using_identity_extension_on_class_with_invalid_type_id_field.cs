using Marten.Testing.Harness;
using Shouldly;
using System;
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

    public class OverriddenIdWithInvalidTypeIdField
    {
        public short Id { get; set; }

        public Guid DocumentId { get; set; }
    }

    public Bug_2327_using_identity_extension_on_class_with_invalid_type_id_field(DefaultStoreFixture fixture)
        : base(fixture) { }
}
