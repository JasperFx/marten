using System;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_628_fk_from_doc_to_itself: IntegrationContext
    {
        public class Category
        {
            public Guid Id { get; set; }
            public Guid? ParentId { get; set; }
            public string Name { get; set; }
        }

        [Fact]
        public void can_reference_itself_as_an_fk()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Category>().ForeignKey<Category>(x => x.ParentId);
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();
        }

        public Bug_628_fk_from_doc_to_itself(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
