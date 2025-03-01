using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Patching;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;
using Xunit;

namespace PatchingTests.Patching;

public class patching_api: OneOffConfigurationsContext
{
    public patching_api()
    {
        StoreOptions(_ =>
        {
            _.UseDefaultSerialization(EnumStorage.AsString);
        });
    }

    #region sample_patching_set_an_immediate_property_by_id

    [Fact]
    public async Task set_an_immediate_property_by_id()
    {
        var target = Target.Random(true);
        target.Number = 5;

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Set(x => x.Number, 10);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).Number.ShouldBe(10);
        }
    }

    #endregion

    [Fact]
    public async Task initialise_a_new_property_by_expression()
    {
        theSession.Store(Target.Random(), Target.Random(), Target.Random());
        await theSession.SaveChangesAsync();

        #region sample_patching_initialise_a_new_property_by_expression
        const string where = "(data ->> 'UpdatedAt') is null";
        theSession.Query<Target>(where).Count.ShouldBe(3);
        theSession.Patch<Target>(new WhereFragment(where)).Set("UpdatedAt", DateTime.UtcNow);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Query<Target>(where).Count.ShouldBe(0);
        }
        #endregion
    }

    [Fact]
    public async Task set_a_deep_property_by_id()
    {
        var target = Target.Random(true);
        target.Inner.Number = 5;

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Set(x => x.Inner.Number, 10);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).Inner.Number.ShouldBe(10);
        }
    }

    [Fact]
    public async Task set_an_immediate_property_by_where_clause()
    {
        var target1 = new Target { Color = Colors.Blue, Number = 1 };
        var target2 = new Target { Color = Colors.Blue, Number = 1 };
        var target3 = new Target { Color = Colors.Blue, Number = 1 };
        var target4 = new Target { Color = Colors.Green, Number = 1 };
        var target5 = new Target { Color = Colors.Green, Number = 1 };
        var target6 = new Target { Color = Colors.Red, Number = 1 };

        theSession.Store(target1, target2, target3, target4, target5, target6);
        await theSession.SaveChangesAsync();

        #region sample_patching_set_an_immediate_property_by_where_clause
        // Change every Target document where the Color is Blue
        theSession.Patch<Target>(x => x.Color == Colors.Blue).Set(x => x.Number, 2);
        #endregion

        await theSession.SaveChangesAsync();

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
    public async Task duplicate_to_new_field()
    {
        #region sample_patching_duplicate_to_new_field
        var target = Target.Random();
        target.AnotherString = null;
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Duplicate(t => t.String, t => t.AnotherString);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var result = query.Load<Target>(target.Id);
            result.AnotherString.ShouldBe(target.String);
        }
        #endregion
    }

    [Fact]
    public async Task duplicate_to_multiple_new_fields()
    {
        var target = Target.Random();
        target.StringField = null;
        target.Inner = null;
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        #region sample_patching_duplicate_to_multiple_new_fields
        theSession.Patch<Target>(target.Id).Duplicate(t => t.String,
            t => t.StringField,
            t => t.Inner.String,
            t => t.Inner.AnotherString);
        #endregion
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var result = query.Load<Target>(target.Id);

            result.StringField.ShouldBe(target.String);
            result.Inner.ShouldNotBeNull();
            result.Inner.String.ShouldBe(target.String);
            result.Inner.AnotherString.ShouldBe(target.String);
        }
    }

    #region sample_patching_increment_for_int
    [Fact]
    public async Task increment_for_int()
    {
        var target = Target.Random();
        target.Number = 6;

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Increment(x => x.Number);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).Number.ShouldBe(7);
        }
    }

    #endregion

    #region sample_patching_increment_for_int_with_explicit_increment
    [Fact]
    public async Task increment_for_int_with_explicit_increment()
    {
        var target = Target.Random();
        target.Number = 6;

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Increment(x => x.Number, 3);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).Number.ShouldBe(9);
        }
    }

    #endregion

    [Fact]
    public async Task increment_for_long()
    {
        var target = Target.Random();
        target.Long = 13;

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Increment(x => x.Long);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).Long.ShouldBe(14);
        }
    }

    [Fact]
    public async Task increment_for_double()
    {
        var target = Target.Random();
        target.Double = 11.2;

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Increment(x => x.Double, 2.4);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).Double.ShouldBe(13.6);
        }
    }

    [Fact]
    public async Task increment_for_float()
    {
        var target = Target.Random();
        target.Float = 11.2F;

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Increment(x => x.Float, 2.4F);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).Float.ShouldBe(13.6F);
        }
    }

    [Fact]
    public async Task increment_for_decimal()
    {
        var target = Target.Random();
        target.Decimal = 11.2m;

        theSession.Store(target);
        theSession.Patch<Target>(target.Id).Increment(x => x.Decimal, 2.4m);
        await theSession.SaveChangesAsync();
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).Decimal.ShouldBe(13.6m);
        }
    }

    [Fact]
    public async Task append_to_a_primitive_array()
    {
        var target = Target.Random();
        target.NumberArray = new[] { 1, 2, 3 };

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Append(x => x.NumberArray, 4);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).NumberArray
                .ShouldHaveTheSameElementsAs(1, 2, 3, 4);
        }
    }

    [Fact]
    public async Task append_if_not_exists_to_a_primitive_array()
    {
        var target = Target.Random();
        target.NumberArray = new[] { 1, 2, 3 };

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).AppendIfNotExists(x => x.NumberArray, 3);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).NumberArray
                .ShouldHaveTheSameElementsAs(1, 2, 3);
        }

        theSession.Patch<Target>(target.Id).AppendIfNotExists(x => x.NumberArray, 4);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).NumberArray
                .ShouldHaveTheSameElementsAs(1, 2, 3, 4);
        }
    }

    #region sample_patching_append_complex_element
    [Fact]
    public async Task append_complex_element()
    {
        var target = Target.Random(true);
        var initialCount = target.Children.Length;

        var child = Target.Random();

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Append(x => x.Children, child);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 1);

            target2.Children.Last().Id.ShouldBe(child.Id);
        }
    }

    #endregion

    [Fact]
    public async Task append_if_not_exists_complex_element()
    {
        var target = Target.Random(true);
        var initialCount = target.Children.Length;

        var child = Target.Random();
        var child2 = Target.Random();

        theSession.Store(target);
        await theSession.SaveChangesAsync();
        theSession.Patch<Target>(target.Id).Append(x => x.Children, child);
        await theSession.SaveChangesAsync();
        theSession.Patch<Target>(target.Id).AppendIfNotExists(x => x.Children, child);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 1);

            target2.Children.Last().Id.ShouldBe(child.Id);
        }

        theSession.Patch<Target>(target.Id).AppendIfNotExists(x => x.Children, child2);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 2);

            target2.Children.Last().Id.ShouldBe(child2.Id);
        }
    }

    [Fact]
    public async Task append_if_not_exists_complex_element_by_predicate()
    {
        var target = Target.Random(true);
        var initialCount = target.Children.Length;

        var child = Target.Random();
        var child2 = Target.Random();

        theSession.Store(target);
        await theSession.SaveChangesAsync();
        theSession.Patch<Target>(target.Id).Append(x => x.Children, child);
        await theSession.SaveChangesAsync();
        theSession.Patch<Target>(target.Id).AppendIfNotExists(x => x.Children, child, x => x.Id == child.Id);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 1);

            target2.Children.Last().Id.ShouldBe(child.Id);
        }

        theSession.Patch<Target>(target.Id).AppendIfNotExists(x => x.Children, child2, x => x.Id == child2.Id);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 2);

            target2.Children.Last().Id.ShouldBe(child2.Id);
        }
    }

    [Fact]
    public async Task insert_first_to_a_primitive_array()
    {
        var target = Target.Random();
        target.NumberArray = new[] { 1, 2, 3 };

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Insert(x => x.NumberArray, 4);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).NumberArray
                .ShouldHaveTheSameElementsAs(1, 2, 3, 4);
        }
    }

    [Fact]
    public async Task insert_if_not_exists_last_to_a_primitive_array()
    {
        var target = Target.Random();
        target.NumberArray = new[] { 1, 2, 3 };

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.NumberArray, 1);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).NumberArray
                .ShouldHaveTheSameElementsAs(1, 2, 3);
        }

        theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.NumberArray, 4);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).NumberArray
                .ShouldHaveTheSameElementsAs(1, 2, 3, 4);
        }
    }

    [Fact]
    public async Task insert_first_to_a_primitive_array_at_a_certain_position()
    {
        var target = Target.Random();
        target.NumberArray = new[] { 1, 2, 3 };

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Insert(x => x.NumberArray, 4, 2);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).NumberArray
                .ShouldHaveTheSameElementsAs(1, 2, 4, 3);
        }
    }

    [Fact]
    public async Task insert_if_not_exists_first_to_a_primitive_array_at_a_certain_position()
    {
        var target = Target.Random();
        target.NumberArray = new[] { 1, 2, 3 };

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.NumberArray, 3, 2);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).NumberArray
                .ShouldHaveTheSameElementsAs(1, 2, 3);
        }

        theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.NumberArray, 4, 2);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).NumberArray
                .ShouldHaveTheSameElementsAs(1, 2, 4, 3);
        }
    }

    #region sample_patching_insert_first_complex_element
    [Fact]
    public async Task insert_first_complex_element()
    {
        var target = Target.Random(true);
        var initialCount = target.Children.Length;

        var child = Target.Random();

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Insert(x => x.Children, child);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 1);

            target2.Children.Last().Id.ShouldBe(child.Id);
        }
    }

    #endregion

    [Fact]
    public async Task insert_if_not_exists_last_complex_element()
    {
        var target = Target.Random(true);
        var initialCount = target.Children.Length;

        var child = Target.Random();
        var child2 = Target.Random();
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Insert(x => x.Children, child);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 1);

            target2.Children.Last().Id.ShouldBe(child.Id);
        }

        theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.Children, child);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 1);

            target2.Children.Last().Id.ShouldBe(child.Id);
        }

        theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.Children, child2);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 2);

            target2.Children.Last().Id.ShouldBe(child2.Id);
        }
    }

    [Fact]
    public async Task insert_if_not_exists_last_complex_element_by_predicate()
    {
        var target = Target.Random(true);
        var initialCount = target.Children.Length;

        var child = Target.Random();
        var child2 = Target.Random();
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Insert(x => x.Children, child);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 1);

            target2.Children.Last().Id.ShouldBe(child.Id);
        }

        theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.Children, child, x => x.Id == child.Id);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 1);

            target2.Children.Last().Id.ShouldBe(child.Id);
        }

        theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.Children, child2, x => x.Id == child2.Id);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount + 2);

            target2.Children.Last().Id.ShouldBe(child2.Id);
        }
    }

    [Fact]
    public async Task rename_shallow_prop()
    {
        var target = Target.Random(true);
        target.String = "Foo";
        target.AnotherString = "Bar";

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Rename("String", x => x.AnotherString);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.AnotherString.ShouldBe("Foo");
            target2.String.ShouldBeNull();
        }
    }

    #region sample_patching_rename_deep_prop
    [Fact]
    public async Task rename_deep_prop()
    {
        var target = Target.Random(true);
        target.Inner.String = "Foo";
        target.Inner.AnotherString = "Bar";

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Rename("String", x => x.Inner.AnotherString);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Inner.AnotherString.ShouldBe("Foo");
            target2.Inner.String.ShouldBeNull();
        }
    }

    #endregion

    #region sample_patching_remove_primitive_element
    [Fact]
    public async Task remove_primitive_element()
    {
        var random = new Random();
        var target = Target.Random();
        target.NumberArray = new[] { random.Next(0, 10), random.Next(0, 10), random.Next(0, 10) };
        target.NumberArray = target.NumberArray.Distinct().ToArray();

        var initialCount = target.NumberArray.Length;


        var child = target.NumberArray[random.Next(0, initialCount)];

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Remove(x => x.NumberArray, child);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.NumberArray.Length.ShouldBe(initialCount - 1);

            target2.NumberArray.ShouldHaveTheSameElementsAs(target.NumberArray.ExceptFirst(child));
        }
    }

    #endregion

    #region sample_patching_remove_repeated_primitive_element
    [Fact]
    public async Task remove_repeated_primitive_elements()
    {
        var random = new Random();
        var target = Target.Random();
        target.NumberArray = new[] { random.Next(0, 10), random.Next(0, 10), random.Next(0, 10) };
        target.NumberArray = target.NumberArray.Distinct().ToArray();

        var initialCount = target.NumberArray.Length;


        var child = target.NumberArray[random.Next(0, initialCount)];
        var occurances = target.NumberArray.Count(e => e == child);
        if (occurances < 2)
        {
            target.NumberArray = target.NumberArray.Concat(new[] { child }).ToArray();
            ++occurances;
            ++initialCount;
        }

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Remove(x => x.NumberArray, child, RemoveAction.RemoveAll);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.NumberArray.Length.ShouldBe(initialCount - occurances);

            target2.NumberArray.ShouldHaveTheSameElementsAs(target.NumberArray.Except(new[] { child }));
        }
    }

    #endregion

    #region sample_patching_remove_complex_element
    [Fact]
    public async Task remove_complex_element()
    {
        var target = Target.Random(true);
        var initialCount = target.Children.Length;

        var random = new Random();
        var child = target.Children[random.Next(0, initialCount)];

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Remove(x => x.Children, child);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var target2 = query.Load<Target>(target.Id);
            target2.Children.Length.ShouldBe(initialCount - 1);

            target2.Children.ShouldNotContain(t => t.Id == child.Id);
        }
    }

    #endregion

    #region sample_patching_remove_complex_element_by_predicate

    [Fact]
    public async Task remove_complex_element_by_predicate()
    {
        var target = Target.Random();
        target.Children = [Target.Random(), Target.Random(), Target.Random(), Target.Random()];
        var initialCount = target.Children.Length;

        var random = new Random();
        var child = target.Children[random.Next(0, initialCount)];

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Remove(x => x.Children, x => x.Id == child.Id);
        await theSession.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var target2 = query.Load<Target>(target.Id);
        target2.Children.Length.ShouldBe(initialCount - 1);
        target2.Children.ShouldNotContain(t => t.Id == child.Id);
    }

    [Fact]
    public async Task remove_complex_elements_by_predicate()
    {
        var target = Target.Random();
        target.Children = [Target.Random(), Target.Random(), Target.Random(), Target.Random()];
        var initialCount = target.Children.Length;

        var random = new Random();
        var child = target.Children[random.Next(0, initialCount)];

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Remove(x => x.Children, x => x.Id != child.Id);
        await theSession.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var target2 = query.Load<Target>(target.Id);
        target2.Children.Length.ShouldBe(1);
        target2.Children.ShouldContain(t => t.Id == child.Id);
    }

    #endregion

    [Fact]
    public async Task remove_complex_nested_elements_by_predicate(){
        var target = Target.Random(true);
        var random = new Random();
        var initiallength = target.NestedObject.Targets.Length;
        var randomitem = target.NestedObject.Targets[random.Next(0, initiallength)];
        
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id).Remove(x => x.NestedObject.Targets, x => x.Id == randomitem.Id);
        await theSession.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var target2 = query.Load<Target>(target.Id);
        target2.NestedObject.Targets.Length.ShouldBe(initiallength - 1);
        target2.NestedObject.Targets.ShouldNotContain(x => x.Id == randomitem.Id);
    }

    [Fact]
    public async Task throw_exception_if_a_method_call_is_used_for_remove_complex_element_by_predicate()
    {
        var target = Target.Random();
        target.Children = [Target.Random(), Target.Random(), Target.Random(), Target.Random()];
        var initialCount = target.Children.Length;

        var random = new Random();
        var child = target.Children[random.Next(0, initialCount)];

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        var call = () => theSession
            .Patch<Target>(target.Id)
            .Remove(x => x.Children, x => x.Id.ToString() == child.Id.ToString());

        call.ShouldThrow<MartenNotSupportedException>("Calling a method is not supported");
    }

    [Fact]
    public async Task delete_redundant_property()
    {
        var target = Target.Random();
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        #region sample_patching_delete_redundant_property
        theSession.Patch<Target>(target.Id).Delete("String");
        #endregion
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var result = query.Load<Target>(target.Id);

            result.String.ShouldBeNull();
        }
    }

    [Fact]
    public async Task delete_redundant_nested_property()
    {
        var target = Target.Random(true);
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        #region sample_patching_delete_redundant_nested_property
        theSession.Patch<Target>(target.Id).Delete("String", t => t.Inner);
        #endregion
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var result = query.Load<Target>(target.Id);

            result.Inner.String.ShouldBeNull();
        }
    }

    [Fact]
    public async Task delete_existing_property()
    {
        var target = Target.Random(true);
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        #region sample_patching_delete_existing_property
        theSession.Patch<Target>(target.Id).Delete(t => t.Inner);
        #endregion
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            var result = query.Load<Target>(target.Id);

            result.Inner.ShouldBeNull();
        }
    }

    [Fact]
    public async Task delete_property_from_many_documents()
    {
        for (var i = 0; i < 15; i++)
        {
            theSession.Store(Target.Random());
        }
        await theSession.SaveChangesAsync();

        #region sample_patching_delete_property_from_many_documents
        const string where = "(data ->> 'String') is not null";
        theSession.Query<Target>(where).Count.ShouldBe(15);
        theSession.Patch<Target>(new WhereFragment(where)).Delete("String");
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Query<Target>(where).Count(t => t.String != null).ShouldBe(0);
        }
        #endregion
    }

    [Fact]
    public async Task bug_611_duplicate_field_is_updated_by_set_operation()
    {
        var mapping = theStore.StorageFeatures.MappingFor(typeof(Target));
        var field = mapping.DuplicateField("String");

        var entity = Target.Random();
        theSession.Store(entity);
        await theSession.SaveChangesAsync();

        var newval = new string(entity.String.Reverse().ToArray());
        theSession.Patch<Target>(entity.Id).Set(t => t.String, newval);
        await theSession.SaveChangesAsync();

        await using var command = theSession.Connection.CreateCommand();
        command.CommandText = $"select count(*) from {mapping.TableName.QualifiedName} " +
                              $"where data->>'String' = '{newval}' and {field.ColumnName} = '{newval}'";
        var count = (long)(command.ExecuteScalar() ?? 0);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task bug_611_duplicate_field_is_updated_by_set_operation_with_multiple_duplicates_smoke_test()
    {
        var mapping = theStore.StorageFeatures.MappingFor(typeof(Target));
        var field = mapping.DuplicateField("String");
        var field2 = mapping.DuplicateField(nameof(Target.Number));

        var entity = Target.Random();
        theSession.Store(entity);
        await theSession.SaveChangesAsync();

        var newval = new string(entity.String.Reverse().ToArray());
        theSession.Patch<Target>(entity.Id).Set(t => t.String, newval);
        await theSession.SaveChangesAsync();

        await using var command = theSession.Connection.CreateCommand();
        command.CommandText = $"select count(*) from {mapping.TableName.QualifiedName} " +
                              $"where data->>'String' = '{newval}' and {field.ColumnName} = '{newval}'";
        var count = (long)(command.ExecuteScalar() ?? 0);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task duplicated_fields_only_update_when_source_is_modified()
    {
        // Set up duplicate field in the schema
        var t = Target.Random();
        var mapping = theStore.StorageFeatures.MappingFor(typeof(Target));
        var duplicateField = mapping.DuplicateField("String");

        // Setup a document
        var target = Target.Random();
        target.Inner = Target.Random();
        target.Inner.String = "original";
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        // First verify that modifying the source updates the duplicate
        var newValue = "modified source";
        theSession.Patch<Target>(target.Id).Set(x => x.String, newValue);
        await theSession.SaveChangesAsync();

        // Verify both fields are updated
        await using (var command = theSession.Connection.CreateCommand())
        {
            command.CommandText = $"select count(*) from {mapping.TableName.QualifiedName} " +
                                  $"where data->>'String' = '{newValue}' and {duplicateField.ToColumn().Name} = '{newValue}'";
            var count = (long)(command.ExecuteScalar() ?? 0);
            count.ShouldBe(1);
        }

        // Now modify an unrelated field and capture the SQL
        var capturedCommands = new List<string>();
        theSession.Logger = new TestLogger(capturedCommands).StartSession(theSession);

        theSession.Patch<Target>(target.Id).Set(x => x.Number, 42);
        await theSession.SaveChangesAsync();

        // Verify no update to the duplicate field was executed
        capturedCommands.Any(sql => sql.Contains($"set {duplicateField.ColumnName}")).ShouldBeFalse();
    }

    [Fact]
    public async Task duplicated_fields_only_update_when_nested_source_is_modified()
    {
        // Set up duplicate field in the schema
        var t = Target.Random();
        var mapping = theStore.StorageFeatures.MappingFor(typeof(Target));
        MemberInfo[] props =
        [
            ReflectionHelper.GetProperty<Target>(x => x.Inner),
            ReflectionHelper.GetProperty<Target>(x => x.String)
        ];
        var duplicateField = mapping.DuplicateField(props, columnName: "String");

        // Setup a document
        var target = Target.Random();
        target.Inner = Target.Random();
        target.Inner.String = "original";
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        // First verify that modifying the source updates the duplicate
        var newValue = "modified source";
        theSession.Patch<Target>(target.Id).Set(x => x.Inner.String, newValue);
        await theSession.SaveChangesAsync();

        // Verify both fields are updated
        await using (var command = theSession.Connection.CreateCommand())
        {
            command.CommandText = $"select count(*) from {mapping.TableName.QualifiedName} " +
                                $"where data->'Inner'->>'String' = '{newValue}' and {duplicateField.ToColumn().Name} = '{newValue}'";
            var count = (long)(command.ExecuteScalar() ?? 0);
            count.ShouldBe(1);
        }

        // Now modify an unrelated field and capture the SQL
        var capturedCommands = new List<string>();
        theSession.Logger = new TestLogger(capturedCommands).StartSession(theSession);

        theSession.Patch<Target>(target.Id).Set(x => x.Number, 42);
        await theSession.SaveChangesAsync();

        // Verify no update to the duplicate field was executed
        capturedCommands.Any(sql => sql.Contains($"set {duplicateField.ColumnName}")).ShouldBeFalse();
    }

    private class TestLogger(List<string> capturedCommands): IMartenLogger
    {
        public IMartenSessionLogger StartSession(IQuerySession session) => new TestSessionLogger(capturedCommands);
        public void SchemaChange(string sql)
        {
        }
    }

    private class TestSessionLogger(List<string> capturedCommands): IMartenSessionLogger
    {
        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
        }

        public void LogFailure(NpgsqlBatch batch, Exception ex)
        {
        }

        public void LogFailure(Exception ex, string message)
        {
        }

        public void LogSuccess(NpgsqlBatch batch)
        {
            foreach (var command in batch.BatchCommands)
            {
                capturedCommands.Add(command.CommandText);
            }
        }

        public void LogSuccess(NpgsqlCommand command)
        {
            capturedCommands.Add(command.CommandText);
        }

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
        }

        public void OnBeforeExecute(NpgsqlCommand command)
        {
        }

        public void OnBeforeExecute(NpgsqlBatch batch)
        {
        }
    }

    public void SampleSetup()
    {
        #region sample_registering_custom_projection

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Use inline lifecycle
            opts.Projections.Add(new QuestPatchTestProjection(), ProjectionLifecycle.Inline);

            // Or use this as an asychronous projection
            opts.Projections.Add(new QuestPatchTestProjection(), ProjectionLifecycle.Async);
        });

        #endregion
    }

    [Theory]
    [InlineData(TenancyStyle.Single)]
    [InlineData(TenancyStyle.Conjoined)]
    public async Task patch_inside_inline_projection_does_not_error_during_savechanges(TenancyStyle tenancyStyle)
    {
        StoreOptions(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;
            _.Events.TenancyStyle = tenancyStyle;

            _.Projections.Add(new QuestPatchTestProjection(), ProjectionLifecycle.Inline);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var aggregateId = Guid.NewGuid();
        var quest = new Quest
        {
            Id = aggregateId,
        };
        var questStarted = new QuestStarted
        {
            Id = aggregateId,
            Name = "New Quest",
        };

        theSession.Events.Append(aggregateId, quest, questStarted);
        await theSession.SaveChangesAsync();

        (await theSession.Events.FetchStreamStateAsync(aggregateId)).Version.ShouldBe(2);
    }

    #region sample_QuestPatchTestProjection

    public class QuestPatchTestProjection: IProjection
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
        {
            var questEvents = streams.SelectMany(x => x.Events).OrderBy(s => s.Sequence).Select(s => s.Data);

            foreach (var @event in questEvents)
            {
                if (@event is Quest quest)
                {
                    operations.Store(new QuestPatchTestProjection { Id = quest.Id });
                }
                else if (@event is QuestStarted started)
                {
                    operations.Patch<QuestPatchTestProjection>(started.Id).Set(x => x.Name, "New Name");
                }
            }
        }

        public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation)
        {
            Apply(operations, streams);
            return Task.CompletedTask;
        }
    }

    #endregion

    #region sample_patching_multiple_fields

    [Fact]
    public async Task able_to_chain_patch_operations()
    {
        var target = Target.Random(true);
        target.Number = 5;

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        theSession.Patch<Target>(target.Id)
            .Set(x => x.Number, 10)
            .Increment(x => x.Number, 10);
        await theSession.SaveChangesAsync();

        using (var query = theStore.QuerySession())
        {
            query.Load<Target>(target.Id).Number.ShouldBe(20);
        }
    }

    #endregion
}

internal static class EnumerableExtensions
{
    public static IEnumerable<T> ExceptFirst<T>(this IEnumerable<T> enumerable, T item)
    {
        var encountered = false;
        var expected = new List<T>();
        foreach (var val in enumerable)
        {
            if (!encountered && val.Equals(item))
            {
                encountered = true;
                continue;
            }
            expected.Add(val);
        }
        return expected;
    }
}

public class Quest
{
    public Guid Id { get; set; }
}

public class QuestStarted
{
    public string Name { get; set; }
    public Guid Id { get; set; }

    public override string ToString()
    {
        return $"Quest {Name} started";
    }

    protected bool Equals(QuestStarted other)
    {
        return Name == other.Name && Id.Equals(other.Id);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((QuestStarted) obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Id);
    }
}
