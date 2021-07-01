using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    [ControlledQueryStoryteller]
    public class Bug_1475_PreserveReferencesHandling: IntegrationContext
    {

        [Fact]
        public void does_not_blow_up_on_store()
        {
            var cyclic = new Cyclic { Id = 1};
            var parent = new Node();

            var child = new Node();
            child.Parent = cyclic.Root;
            parent.Child = child;

            cyclic.Root = parent;

            theSession.Store(cyclic);
            theSession.SaveChanges();
        }

        [Fact]
        public void does_not_blow_up_on_load()
        {
            var cyclic = new Cyclic { Id = 2 };
            var parent = new Node();

            var child = new Node();
            child.Parent = parent;
            parent.Child = child;

            cyclic.Root = parent;

            // prove it works before serialization
            cyclic.Root.Child.Parent.Child.ShouldBeSameAs(cyclic.Root.Child);

            theSession.Store(cyclic);
            theSession.SaveChanges();

            var loadedCyclic = theSession.Load<Cyclic>(2);
            loadedCyclic.Root.Child.Parent.Child.ShouldBeSameAs(loadedCyclic.Root.Child);
        }

        [Fact]
        public void does_not_blow_up_on_query_by_property()
        {
            var cyclic = new Cyclic { Id = 2, Name = "Bicycle" };
            var parent = new Node();

            var child = new Node();
            child.Parent = parent;
            parent.Child = child;

            cyclic.Root = parent;

            // prove it works before serialization
            cyclic.Root.Child.Parent.Child.ShouldBeSameAs(cyclic.Root.Child);

            theSession.Store(cyclic);
            theSession.SaveChanges();

            var loadedCyclic = theSession.Query<Cyclic>().SingleOrDefault(c => c.Name == "Bicycle");
            loadedCyclic.Root.Child.Parent.Child.ShouldBeSameAs(loadedCyclic.Root.Child);
        }

        [Fact]
        public void does_not_blow_up_on_deletewhere()
        {
            var cyclic = new Cyclic { Id = 2 };
            var parent = new Node();

            var child = new Node();
            child.Parent = cyclic.Root;
            parent.Child = child;

            cyclic.Root = parent;

            theSession.Store(cyclic);
            theSession.SaveChanges();

            theSession.DeleteWhere<Cyclic>(c => c.Id == 2);

            theSession.Query<Cyclic>().Count().ShouldBe(0);
        }

        public Bug_1475_PreserveReferencesHandling(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(o =>
            {
                var serializer = new JsonNetSerializer();
                serializer.Customize(c => c.PreserveReferencesHandling = PreserveReferencesHandling.Objects);
                o.Serializer(serializer);
            });
        }
    }
}
