using Marten.Schema;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class using_natural_identity_keys : IntegratedFixture
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
        public void can_persist_with_natural_key()
        {
            var doc = new NonStandardDoc {Name = "somebody"};

            using (var session = theStore.OpenSession())
            {
                session.Store(doc);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Load<NonStandardDoc>("somebody").ShouldNotBeNull();
            }
        }
    }

    public class NonStandardDoc
    {
        [Identity]
        public string Name;
    }

    public class NonStandardWithProp
    {
        [Identity]
        public string Name { get; set; }
    }
}