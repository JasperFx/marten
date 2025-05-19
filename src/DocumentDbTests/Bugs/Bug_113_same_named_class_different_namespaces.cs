using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs
{
    public class Bug_113_same_named_class_different_namespaces: BugIntegrationContext
    {
        #region sample_can_select_from_the_same_table
        [Fact]
        public async Task can_select_from_the_same_table()
        {
            using var session = theStore.LightweightSession();
            var product1 = new Area1.Product { Name = "Paper", Price = 10 };
            session.Store(product1);
            await session.SaveChangesAsync();

            var product2 = await session.LoadAsync<Area2.Product>(product1.Id);

            product2.Name.ShouldBe(product1.Name);
        }

        #endregion

    }

    #region sample_structural_typing_classes
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

    #endregion
}
