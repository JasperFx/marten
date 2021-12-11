using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class ejecting_a_document_type : IntegrationContext
    {
        [Fact]
        public void eject_a_document_type_clears_it_from_the_identity_map_regular()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.OpenSession())
            {
                session.Store(target1, target2);

                session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
                session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);

                session.EjectAllOfType(typeof(Target));

                SpecificationExtensions.ShouldBeNull(session.Load<Target>(target1.Id));
                SpecificationExtensions.ShouldBeNull(session.Load<Target>(target2.Id));
            }
        }

        [Fact]
        public void eject_a_document_type_clears_it_from_the_identity_map_dirty()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.DirtyTrackedSession())
            {
                session.Store(target1, target2);

                session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
                session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);

                session.EjectAllOfType(typeof(Target));

                SpecificationExtensions.ShouldBeNull(session.Load<Target>(target1.Id));
                SpecificationExtensions.ShouldBeNull(session.Load<Target>(target2.Id));
            }
        }

        [Fact]
        public void eject_a_document_type_clears_it_from_the_unit_of_work_regular()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.OpenSession())
            {
                session.Store(target1, target2);

                session.EjectAllOfType(typeof(Target));

                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().ShouldBeEmpty();
            }
        }

        [Fact]
        public void eject_a_document_type_clears_it_from_the_unit_of_work_dirty()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.DirtyTrackedSession())
            {
                session.Store(target1, target2);

                session.EjectAllOfType(typeof(Target));

                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().ShouldBeEmpty();
            }
        }

        [Fact]
        public void eject_a_document_type_clears_it_from_the_unit_of_work_lightweight()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2);

                session.EjectAllOfType(typeof(Target));

                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().ShouldBeEmpty();
            }
        }

        public ejecting_a_document_type(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
