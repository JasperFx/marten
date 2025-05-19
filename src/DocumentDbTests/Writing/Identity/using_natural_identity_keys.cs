using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity;

public class using_natural_identity_keys: IntegrationContext
{
    [Fact]
    public void finds_the_id_member_with_the_attribute_on_a_field()
    {
        var mapping = DocumentMapping.For<NonStandardDoc>();
        mapping.IdMember.Name.ShouldBe(nameof(NonStandardDoc.Name));
    }

    [Fact]
    public void finds_the_id_member_with_the_attribute_on_a_property()
    {
        var mapping = DocumentMapping.For<NonStandardWithProp>();
        mapping.IdMember.Name.ShouldBe(nameof(NonStandardWithProp.Name));
    }

    [Fact]
    public void finds_the_right_id_member_for_doc_with_both_id_column_and_identity_attribute()
    {
        var mapping = DocumentMapping.For<IdAndIdentityAttDoc>();
        mapping.IdMember.Name.ShouldBe(nameof(IdAndIdentityAttDoc.DocumentId));
    }

    [Fact]
    public async Task can_persist_with_natural_key()
    {
        var doc = new NonStandardDoc { Name = "somebody" };

        using (var session = theStore.LightweightSession())
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<NonStandardDoc>("somebody")).ShouldNotBeNull();
        }
    }

    [Fact]
    public void can_override_the_identity_member_with_the_fluent_interface()
    {
        StoreOptions(storeOptions =>
        {
            #region sample_sample-override-id-fluent-interance
            storeOptions.Schema.For<OverriddenIdDoc>().Identity(x => x.Name);
            #endregion
        });

        var mapping = theStore.StorageFeatures.MappingFor(typeof(OverriddenIdDoc)).As<DocumentMapping>();
        mapping.IdType.ShouldBe(typeof(string));
        mapping.IdMember.Name.ShouldBe(nameof(OverriddenIdDoc.Name));
        mapping.IdStrategy.ShouldBeOfType<StringIdGeneration>();
    }

    public using_natural_identity_keys(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}

#region sample_IdentityAttribute
public class NonStandardDoc
{
    [Identity]
    public string Name;
}

#endregion

public class NonStandardWithProp
{
    [Identity]
    public string Name { get; set; }
}

public class OverriddenIdDoc
{
    public string Name { get; set; }

    public DateTime Date { get; set; }
}

public class IdAndIdentityAttDoc
{
    public Guid Id { get; set; }

    [Identity]
    public Guid DocumentId { get; set; }
}
