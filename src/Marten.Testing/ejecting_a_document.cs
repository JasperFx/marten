using System.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class ejecting_a_document : IntegratedFixture
    {
        // SAMPLE: ejecting_a_document
        [Fact]
        public void demonstrate_eject()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.OpenSession())
            {
                session.Store(target1, target2);

                // Both documents are in the identity map
                session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
                session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);

                // Eject the 2nd document
                session.Eject(target2);

                // Now that 2nd document is no longer in the identity map
                session.Load<Target>(target2.Id).ShouldBeNull();
                
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                // The 2nd document was ejected before the session
                // was saved, so it was never persisted
                session.Load<Target>(target2.Id).ShouldBeNull();
            }
        }
        // ENDSAMPLE
        
        [Fact]
        public void eject_a_document_clears_it_from_the_identity_map_regular()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.OpenSession())
            {
                session.Store(target1, target2);

                session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
                session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);

                session.Eject(target2);

                session.Load<Target>(target2.Id).ShouldBeNull();
            }
        }

        [Fact]
        public void eject_a_document_clears_it_from_the_identity_map_dirty()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.DirtyTrackedSession())
            {
                session.Store(target1, target2);

                session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
                session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);

                session.Eject(target2);

                session.Load<Target>(target2.Id).ShouldBeNull();
            }
        }

        [Fact]
        public void eject_a_document_clears_it_from_the_unit_of_work_regular()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.OpenSession())
            {
                session.Store(target1, target2);

                session.Eject(target2);

                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().Single()
                    .Id.ShouldBe(target1.Id);
            }
        }

        [Fact]
        public void eject_a_document_clears_it_from_the_unit_of_work_dirty()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.DirtyTrackedSession())
            {
                session.Store(target1, target2);

                session.Eject(target2);

                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().Single()
                    .Id.ShouldBe(target1.Id);
            }
        }

        [Fact]
        public void eject_a_document_clears_it_from_the_unit_of_work_lightweight()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2);

                session.Eject(target2);

                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().Single()
                    .Id.ShouldBe(target1.Id);
            }
        }
    }
}