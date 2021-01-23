using System;
using System.Collections.Generic;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.Filters;
using Marten.Patching;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Patching
{
    public class PatchExpressionTests : IntegrationContext
    {
        private readonly PatchExpression<Target> _expression;
        private readonly ITenant _schema = Substitute.For<ITenant>();


        public PatchExpressionTests(DefaultStoreFixture fixture) : base(fixture)
        {
            var storage = Substitute.For<IDocumentStorage>();
            storage.DocumentType.Returns(typeof(Target));

            var session = theStore.LightweightSession();

            Disposables.Add(session);

            _expression = new PatchExpression<Target>(new ByGuidFilter(Guid.NewGuid()), (DocumentSessionBase)session);
        }

        [Fact]
        public void builds_patch_for_set_name()
        {
            _expression.Set("Float", 7.7f);

            _expression.Patch["path"].ShouldBe("Float");
            _expression.Patch["type"].ShouldBe("set");
            _expression.Patch["value"].ShouldBe(7.7f);
        }

        [Fact]
        public void builds_patch_for_set_name_deep()
        {
            _expression.Set("Double", x => x.Inner, 99.9d);

            _expression.Patch["path"].ShouldBe("Inner.Double");
            _expression.Patch["type"].ShouldBe("set");
            _expression.Patch["value"].ShouldBe(99.9d);
        }

        [Fact]
        public void builds_patch_for_set_shallow()
        {
            _expression.Set(x => x.Number, 5);

            _expression.Patch["path"].ShouldBe("Number");
            _expression.Patch["type"].ShouldBe("set");
            _expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void builds_patch_for_set_2_deep()
        {
            _expression.Set(x => x.Inner.Number, 5);

            _expression.Patch["path"].ShouldBe("Inner.Number");
            _expression.Patch["type"].ShouldBe("set");
            _expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void builds_patch_for_set_3_deep()
        {
            _expression.Set(x => x.Inner.Inner.Number, 5);

            _expression.Patch["path"].ShouldBe("Inner.Inner.Number");
            _expression.Patch["type"].ShouldBe("set");
            _expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void increment_int_with_default()
        {
            _expression.Increment(x => x.Number);

            _expression.Patch["path"].ShouldBe("Number");
            _expression.Patch["type"].ShouldBe("increment");
            _expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_int_with_default_deep()
        {
            _expression.Increment(x => x.Inner.Inner.Number);

            _expression.Patch["path"].ShouldBe("Inner.Inner.Number");
            _expression.Patch["type"].ShouldBe("increment");
            _expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_int_with_explicit_interval()
        {
            _expression.Increment(x => x.Number, 5);

            _expression.Patch["path"].ShouldBe("Number");
            _expression.Patch["type"].ShouldBe("increment");
            _expression.Patch["increment"].ShouldBe(5);
        }

        [Fact]
        public void increment_long_with_default()
        {
            _expression.Increment(x => x.Long);

            _expression.Patch["path"].ShouldBe("Long");
            _expression.Patch["type"].ShouldBe("increment");
            _expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_long_with_default_deep()
        {
            _expression.Increment(x => x.Inner.Inner.Long);

            _expression.Patch["path"].ShouldBe("Inner.Inner.Long");
            _expression.Patch["type"].ShouldBe("increment");
            _expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_long_with_explicit_interval()
        {
            _expression.Increment(x => x.Long, 5);

            _expression.Patch["path"].ShouldBe("Long");
            _expression.Patch["type"].ShouldBe("increment");
            _expression.Patch["increment"].ShouldBe(5);
        }

        [Fact]
        public void increment_double_with_default()
        {
            _expression.Increment(x => x.Double);

            _expression.Patch["path"].ShouldBe("Double");
            _expression.Patch["type"].ShouldBe("increment_float");
            _expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_double_with_default_deep()
        {
            _expression.Increment(x => x.Inner.Inner.Double);

            _expression.Patch["path"].ShouldBe("Inner.Inner.Double");
            _expression.Patch["type"].ShouldBe("increment_float");
            _expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_double_with_explicit_interval()
        {
            _expression.Increment(x => x.Double, 5);

            _expression.Patch["path"].ShouldBe("Double");
            _expression.Patch["type"].ShouldBe("increment_float");
            _expression.Patch["increment"].ShouldBe(5);
        }

        [Fact]
        public void increment_float_with_default()
        {
            _expression.Increment(x => x.Float);

            _expression.Patch["path"].ShouldBe("Float");
            _expression.Patch["type"].ShouldBe("increment_float");
            _expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_float_with_default_deep()
        {
            _expression.Increment(x => x.Inner.Inner.Float);

            _expression.Patch["path"].ShouldBe("Inner.Inner.Float");
            _expression.Patch["type"].ShouldBe("increment_float");
            _expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_float_with_explicit_interval()
        {
            _expression.Increment(x => x.Float, 5);

            _expression.Patch["path"].ShouldBe("Float");
            _expression.Patch["type"].ShouldBe("increment_float");
            _expression.Patch["increment"].ShouldBe(5);
        }

        [Fact]
        public void append_shallow()
        {
            _expression.Append(x => x.NumberArray, 5);

            _expression.Patch["path"].ShouldBe("NumberArray");
            _expression.Patch["type"].ShouldBe("append");
            _expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void append_if_not_exists_shallow()
        {
            _expression.AppendIfNotExists(x => x.NumberArray, 5);

            _expression.Patch["path"].ShouldBe("NumberArray");
            _expression.Patch["type"].ShouldBe("append_if_not_exists");
            _expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void append_deep()
        {
            _expression.Append(x => x.Inner.Inner.NumberArray, 5);

            _expression.Patch["path"].ShouldBe("Inner.Inner.NumberArray");
            _expression.Patch["type"].ShouldBe("append");
            _expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void append_if_not_exists_deep()
        {
            _expression.AppendIfNotExists(x => x.Inner.Inner.NumberArray, 5);

            _expression.Patch["path"].ShouldBe("Inner.Inner.NumberArray");
            _expression.Patch["type"].ShouldBe("append_if_not_exists");
            _expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void insert_shallow()
        {
            _expression.Insert(x => x.NumberArray, 5);

            _expression.Patch["path"].ShouldBe("NumberArray");
            _expression.Patch["type"].ShouldBe("insert");
            _expression.Patch["value"].ShouldBe(5);
            _expression.Patch["index"].ShouldBe(0);
        }

        [Fact]
        public void insert_if_not_exists_shallow()
        {
            _expression.InsertIfNotExists(x => x.NumberArray, 5);

            _expression.Patch["path"].ShouldBe("NumberArray");
            _expression.Patch["type"].ShouldBe("insert_if_not_exists");
            _expression.Patch["value"].ShouldBe(5);
            _expression.Patch["index"].ShouldBe(0);
        }

        [Fact]
        public void insert_deep()
        {
            _expression.Insert(x => x.Inner.Inner.NumberArray, 5);

            _expression.Patch["path"].ShouldBe("Inner.Inner.NumberArray");
            _expression.Patch["type"].ShouldBe("insert");
            _expression.Patch["value"].ShouldBe(5);
            _expression.Patch["index"].ShouldBe(0);
        }

        [Fact]
        public void insert_if_not_exists_deep()
        {
            _expression.InsertIfNotExists(x => x.Inner.Inner.NumberArray, 5);

            _expression.Patch["path"].ShouldBe("Inner.Inner.NumberArray");
            _expression.Patch["type"].ShouldBe("insert_if_not_exists");
            _expression.Patch["value"].ShouldBe(5);
            _expression.Patch["index"].ShouldBe(0);
        }


        [Fact]
        public void insert_at_a_nonzero_index()
        {
            _expression.Insert(x => x.NumberArray, 5, 2);

            _expression.Patch["path"].ShouldBe("NumberArray");
            _expression.Patch["type"].ShouldBe("insert");
            _expression.Patch["value"].ShouldBe(5);
            _expression.Patch["index"].ShouldBe(2);
        }

        [Fact]
        public void rename_shallow()
        {
            _expression.Rename("Old", x => x.Double);

            _expression.Patch["type"].ShouldBe("rename");
            _expression.Patch["to"].ShouldBe("Double");
            _expression.Patch["path"].ShouldBe("Old");
        }

        [Fact]
        public void rename_2_deep()
        {
            _expression.Rename("Old", x => x.Inner.Double);

            _expression.Patch["type"].ShouldBe("rename");
            _expression.Patch["to"].ShouldBe("Double");
            _expression.Patch["path"].ShouldBe("Inner.Old");
        }

        [Fact]
        public void rename_3_deep()
        {
            _expression.Rename("Old", x => x.Inner.Inner.Double);

            _expression.Patch["type"].ShouldBe("rename");
            _expression.Patch["to"].ShouldBe("Double");
            _expression.Patch["path"].ShouldBe("Inner.Inner.Old");
        }

        [Fact]
        public void remove_first()
        {
            _expression.Remove(x => x.NumberArray, 5);

            _expression.Patch["type"].ShouldBe("remove");
            _expression.Patch["value"].ShouldBe(5);
            _expression.Patch["path"].ShouldBe("NumberArray");
            _expression.Patch["action"].ShouldBe((int)RemoveAction.RemoveFirst);
        }

        [Fact]
        public void remove_all()
        {
            _expression.Remove(x => x.NumberArray, 5, RemoveAction.RemoveAll);

            _expression.Patch["type"].ShouldBe("remove");
            _expression.Patch["value"].ShouldBe(5);
            _expression.Patch["path"].ShouldBe("NumberArray");
            _expression.Patch["action"].ShouldBe((int) RemoveAction.RemoveAll);
        }

        [Fact]
        public void delete_name()
        {
            _expression.Delete("Foo");

            _expression.Patch["type"].ShouldBe("delete");
            _expression.Patch["path"].ShouldBe("Foo");
        }

        [Fact]
        public void delete_nested_name()
        {
            _expression.Delete("Foo", x => x.Inner.Inner);

            _expression.Patch["type"].ShouldBe("delete");
            _expression.Patch["path"].ShouldBe("Inner.Inner.Foo");
        }

        [Fact]
        public void delete_nested_property()
        {
            _expression.Delete(x => x.NumberArray);

            _expression.Patch["type"].ShouldBe("delete");
            _expression.Patch["path"].ShouldBe("NumberArray");
        }

        [Fact]
        public void duplicate_property()
        {
            _expression.Duplicate(x => x.String, x => x.AnotherString);

            _expression.Patch["type"].ShouldBe("duplicate");
            _expression.Patch["path"].ShouldBe("String");
            ((string[]) _expression.Patch["targets"]).ShouldHaveTheSameElementsAs("AnotherString");
        }

        [Fact]
        public void duplicate_property_to_multiple_targets()
        {
            _expression.Duplicate(x => x.String, x => x.AnotherString, x => x.Inner.String, x => x.Inner.AnotherString);

            _expression.Patch["type"].ShouldBe("duplicate");
            _expression.Patch["path"].ShouldBe("String");
            ((string[])_expression.Patch["targets"]).ShouldHaveTheSameElementsAs("AnotherString", "Inner.String", "Inner.AnotherString");
        }

        [Fact]
        public void duplicate_property_no_target()
        {
            SpecificationExtensions.ShouldContain(Assert.Throws<ArgumentException>(() => _expression.Duplicate(x => x.String))
                    .Message, "At least one destination must be given");
        }

        [Fact]
        public void check_camel_case_serialized_property()
        {
            StoreOptions(_ =>
            {
                _.UseDefaultSerialization(casing: Casing.CamelCase);
            });

            using var session = theStore.LightweightSession();

            var expressionWithSimpleProperty = new PatchExpression<Target>(new ByGuidFilter(Guid.NewGuid()), (DocumentSessionBase) session);
            expressionWithSimpleProperty.Set(x => x.Color, Colors.Blue);
            expressionWithSimpleProperty.Patch["path"].ShouldBe("color");

            var expressionWithNestedProperty = new PatchExpression<Target>(new ByGuidFilter(Guid.NewGuid()), (DocumentSessionBase) session);
            expressionWithNestedProperty.Delete(x => x.Inner.AnotherString);
            expressionWithNestedProperty.Patch["path"].ShouldBe("inner.anotherString");
        }

        public class Item
        {
            public string Name { get; set; }
        }

        public class ColoredItem: Item
        {
            public string Color { get; set; }
        }

        public class NumberedItem: Item
        {
            public int Number { get; set; }
        }

        public class ItemGroup
        {
            public Guid Id { get; set; }
            public List<Item> Items { get; set; } = new List<Item>();
        }

        [Fact]
        public void can_append_with_sub_types_in_collection()
        {
            var group = new ItemGroup();
            theSession.Store(group);
            theSession.SaveChanges();

            using (var session = theStore.LightweightSession())
            {
                session.Patch<ItemGroup>(group.Id).Append(x => x.Items, new Item{Name = "One"});
                session.Patch<ItemGroup>(group.Id).Append(x => x.Items, new ColoredItem{Name = "Two", Color = "Blue"});
                session.Patch<ItemGroup>(group.Id).Append(x => x.Items, new NumberedItem(){Name = "Three", Number = 3});
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var group2 = query.Load<ItemGroup>(group.Id);

                group2.Items.Count.ShouldBe(3);
                group2.Items[0].ShouldBeOfType<Item>();
                group2.Items[1].ShouldBeOfType<ColoredItem>();
                group2.Items[2].ShouldBeOfType<NumberedItem>();
            }
        }

        [Fact]
        public void can_append_if_not_exists_with_sub_types_in_collection()
        {
            var group = new ItemGroup();
            theSession.Store(group);
            theSession.SaveChanges();

            using (var session = theStore.LightweightSession())
            {
                session.Patch<ItemGroup>(group.Id).AppendIfNotExists(x => x.Items, new Item{Name = "One"});
                session.Patch<ItemGroup>(group.Id).AppendIfNotExists(x => x.Items, new ColoredItem{Name = "Two", Color = "Blue"});
                session.Patch<ItemGroup>(group.Id).AppendIfNotExists(x => x.Items, new NumberedItem(){Name = "Three", Number = 3});
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var group2 = query.Load<ItemGroup>(group.Id);

                group2.Items.Count.ShouldBe(3);
                group2.Items[0].ShouldBeOfType<Item>();
                group2.Items[1].ShouldBeOfType<ColoredItem>();
                group2.Items[2].ShouldBeOfType<NumberedItem>();
            }
        }

        [Fact]
        public void can_insert_if_not_exists_with_sub_types_in_collection()
        {
            var group = new ItemGroup
            {
                Items = new List<Item>{new Item{Name = "regular"}}
            };
            theSession.Store(group);
            theSession.SaveChanges();

            using (var session = theStore.LightweightSession())
            {
                session.Patch<ItemGroup>(group.Id).Insert(x => x.Items, new ColoredItem{Name = "Two", Color = "Blue"});
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var group2 = query.Load<ItemGroup>(group.Id);

                group2.Items.Count.ShouldBe(2);
                group2.Items[0].ShouldBeOfType<ColoredItem>();
            }
        }
    }
}
