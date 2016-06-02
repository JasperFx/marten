using System.Linq;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class patching_api : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void set_an_immediate_property_by_id()
        {
            var target = Target.Random(true);
            target.Number = 5;


            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Set(x => x.Number, 10);
            theSession.SaveChanges();


            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Number.ShouldBe(10);
            }
        }

        [Fact]
        public void set_a_deep_property_by_id()
        {
            var target = Target.Random(true);
            target.Inner.Number = 5;


            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Set(x => x.Inner.Number, 10);
            theSession.SaveChanges();


            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Inner.Number.ShouldBe(10);
            }
        }

        [Fact]
        public void set_an_immediate_project_by_where_clause()
        {
            var target1 = new Target {Color = Colors.Blue, Number = 1};
            var target2 = new Target {Color = Colors.Blue, Number = 1};
            var target3 = new Target {Color = Colors.Blue, Number = 1};
            var target4 = new Target {Color = Colors.Green, Number = 1};
            var target5 = new Target {Color = Colors.Green, Number = 1};
            var target6 = new Target {Color = Colors.Red, Number = 1};

            theSession.Store(target1, target2, target3, target4, target5, target6);
            theSession.SaveChanges();

            theSession.Patch<Target>(x => x.Color == Colors.Blue).Set(x => x.Number, 2);
            theSession.SaveChanges();


            using (var query = theStore.QuerySession())
            {
                // These should have been updated
                query.Load<Target>(target1.Id).Number.ShouldBe(2);
                query.Load<Target>(target2.Id).Number.ShouldBe(2);
                query.Load<Target>(target3.Id).Number.ShouldBe(2);

                // These should not because they didn't match the where clause
                query.Load<Target>(target4.Id).Number.ShouldBe(1);
                query.Load<Target>(target5.Id).Number.ShouldBe(1);
                query.Load<Target>(target6.Id).Number.ShouldBe(1);
            }
        }

        [Fact]
        public void increment_for_int()
        {
            var target = Target.Random();
            target.Number = 6;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Increment(x => x.Number);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Number.ShouldBe(7);
            }
        }


        [Fact]
        public void increment_for_int_with_explicit_increment()
        {
            var target = Target.Random();
            target.Number = 6;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Increment(x => x.Number, 3);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Number.ShouldBe(9);
            }
        }

        [Fact]
        public void increment_for_long()
        {
            var target = Target.Random();
            target.Long = 13;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Increment(x => x.Long);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Long.ShouldBe(14);
            }
        }

        [Fact]
        public void increment_for_double()
        {
            var target = Target.Random();
            target.Double = 11.2;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Increment(x => x.Double, 2.4);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Double.ShouldBe(13.6);
            }
        }

        [Fact]
        public void increment_for_float()
        {
            var target = Target.Random();
            target.Float = 11.2F;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Increment(x => x.Float, 2.4F);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Float.ShouldBe(13.6F);
            }
        }

        [Fact]
        public void append_to_a_primitive_array()
        {
            var target = Target.Random();
            target.NumberArray = new[] {1, 2, 3};

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Append(x => x.NumberArray, 4);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(1, 2, 3, 4);
            }
        }

        [Fact]
        public void append_complex_element()
        {
            var target = Target.Random(true);
            var initialCount = target.Children.Length;

            var child = Target.Random();

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Append(x => x.Children, child);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Children.Length.ShouldBe(initialCount + 1);

                target2.Children.Last().Id.ShouldBe(child.Id);
            }
        }

        [Fact]
        public void insert_first_to_a_primitive_array()
        {
            var target = Target.Random();
            target.NumberArray = new[] { 1, 2, 3 };

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Insert(x => x.NumberArray, 4);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(4, 1, 2, 3);
            }
        }

        [Fact]
        public void insert_first_to_a_primitive_array_at_a_certain_position()
        {
            var target = Target.Random();
            target.NumberArray = new[] { 1, 2, 3 };

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Insert(x => x.NumberArray, 4, 2);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(1, 2, 4, 3);
            }
        }

        [Fact]
        public void insert_first_complex_element()
        {
            var target = Target.Random(true);
            var initialCount = target.Children.Length;

            var child = Target.Random();

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Insert(x => x.Children, child);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Children.Length.ShouldBe(initialCount + 1);

                target2.Children.First().Id.ShouldBe(child.Id);
            }
        }

        [Fact]
        public void rename_shallow_prop()
        {
            var target = Target.Random(true);
            target.String = "Foo";
            target.AnotherString = "Bar";

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Rename("String", x => x.AnotherString);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.AnotherString.ShouldBe("Foo");
                target2.String.ShouldBeNull();
            }
        }

        [Fact]
        public void rename_deep_prop()
        {
            var target = Target.Random(true);
            target.Inner.String = "Foo";
            target.Inner.AnotherString = "Bar";

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Rename("String", x => x.Inner.AnotherString);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Inner.AnotherString.ShouldBe("Foo");
                target2.Inner.String.ShouldBeNull();
            }
        }

    }
}