using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Marten.Testing.Patching
{
    public class PatchingTests: IntegratedFixture
    {
        class Model
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }       

        [Fact]
        public async Task Patch_And_Load_Should_Return_Non_Stale_Result()
        {
            var id = Guid.NewGuid();
            using (var sess = theStore.OpenSession())
            {
                sess.Store(new Model() { Id = id, Name = "foo" });
                sess.Patch<Model>(id).Set(x => x.Name, "bar");
                await sess.SaveChangesAsync();
                sess.Query<Model>().Where(x => x.Id == id).Select(x => x.Name).Single().ShouldBe("bar");
                sess.Load<Model>(id).Name.ShouldBe("bar"); 
            }
        }
    }
}
