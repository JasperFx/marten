using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_113_same_named_class_different_namespaces: IntegrationContext
    {
        // SAMPLE: can_select_from_the_same_table
        [Fact]
        public void can_select_from_the_same_table()
        {
            using (var session = theStore.OpenSession())
            {
                var product1 = new Area1.Product { Name = "Paper", Price = 10 };
                session.Store(product1);
                session.SaveChanges();

                var product2 = session.Load<Area2.Product>(product1.Id);

                product2.Name.ShouldBe(product1.Name);
            }
        }

        // ENDSAMPLE
        public Bug_113_same_named_class_different_namespaces(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    // SAMPLE: structural_typing_classes
    namespace Area1
    {
        public class Product
        {
            public int Id { get; set; }

            public string Name { get; set; }

            public decimal Price { get; set; }
        }
    }

    namespace Area2
    {
        [StructuralTyped]
        public class Product
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }
    }

    // ENDSAMPLE
}
