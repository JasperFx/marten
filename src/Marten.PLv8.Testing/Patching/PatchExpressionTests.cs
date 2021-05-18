using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.Filters;
using Marten.PLv8.Patching;
using Marten.PLv8.Transforms;
using Marten.Schema;
using Marten.Services.Json;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.PLv8.Testing.Patching
{
    [Collection("patching")]
    public class PatchExpressionTests : OneOffConfigurationsContext
    {
        private readonly PatchExpression<Target> _expression;
        private readonly ITenant _schema = Substitute.For<ITenant>();



        public PatchExpressionTests() : base("patching")
        {
            StoreOptions(x => x.UseJavascriptTransformsAndPatching());
            theStore.Tenancy.Default.EnsureStorageExists(typeof(TransformSchema));

            var storage = Substitute.For<IDocumentStorage>();
            storage.DocumentType.Returns(typeof(Target));

            var session = theStore.LightweightSession();

            Disposables.Add(session);

            _expression = new PatchExpression<Target>(new ByGuidFilter(Guid.NewGuid()), (DocumentSessionBase)session);
        }

        [Fact]
        public async Task does_not_blow_up()
        {
            var transform = theStore.Tenancy.Default.TransformFor("patch_doc");

            (await theStore.Tenancy.Default.Functions())
                .ShouldContain(transform.Identifier);
        }

        [Fact]
        public async Task Patch_And_Load_Should_Return_Non_Stale_Result()
        {

            var id = Guid.NewGuid();
            using (var sess = theStore.LightweightSession())
            {
                sess.Store(new Model() { Id = id, Name = "foo" });
                sess.Patch<Model>(id).Set(x => x.Name, "bar");
                await sess.SaveChangesAsync();
                sess.Query<Model>().Where(x => x.Id == id).Select(x => x.Name).Single().ShouldBe("bar");
                sess.Load<Model>(id).Name.ShouldBe("bar");
            }
        }

        public class Model
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
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
                _.UseJavascriptTransformsAndPatching();
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

        [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
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

        [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
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

        [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
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


        [Fact]
        public void save_large_bundle_of_operations()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                session.DeleteWhere<TestModel5>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage > 1);

                session.Patch<TestModel5>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage == 1)
                    .Set(x => x.Name, "Matrix 1");

                session.DeleteWhere<TestModel6>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage > 1);

                session.Patch<TestModel6>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage == 1)
                    .Set(x => x.Text, "");

                Expression<Func<TestModel4, bool>> definitionFilter = x => x.ObjectId == id1 && x.DefinitionId == 1;
                session.Patch(definitionFilter).Set(x => x.Stages, 1);
                session.Patch(definitionFilter).Set(x => x.Bool5, false);
                session.Patch(definitionFilter).Set(x => x.Text, "");
                session.Patch(definitionFilter).Set(x => x.Bool4, false);
                session.Patch(definitionFilter).Set(x => x.Mode, "Automatic");
                session.Patch(definitionFilter).Set(x => x.Bool3, true);
                session.Patch(definitionFilter).Set(x => x.Bool1, true);
                session.Patch(definitionFilter).Set(x => x.Bool2, true);
                session.Patch(definitionFilter).Set(x => x.Fields1, "||");
                session.Patch(definitionFilter).Set(x => x.Fields2, "||");
                session.Patch(definitionFilter).Set(x => x.Attr, "|gwk-id|pt-id|");

                session.DeleteWhere<TestModel3>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage > 1);
                session.Patch<TestModel3>(x => x.ObjectId == id1 && x.DefinitionId == 1)
                    .Set(x => x.Cond1, 1);
                session.Patch<TestModel3>(x => x.ObjectId == id1 && x.DefinitionId == 1)
                    .Set(x => x.Cond2, 1);

                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test2");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test3");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test4");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test5");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test6");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test7");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test8");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test9");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test10");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test11");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test12");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test13");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test14");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test15");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test16");
                session.Patch<TestModel2>(Guid.NewGuid()).Set(x => x.Content, "test17");

                session.DeleteWhere<TestModel1>(x => x.ObjectId == id1 && x.DefinitionId == 1 && x.Stage > 1);
                session.Patch<TestModel1>(x => x.ObjectId == id1 && x.DefinitionId == 1)
                    .Set(x => x.Mode, 1);

                session.SaveChanges();
            }
        }

        [DocumentAlias("testmodel1")]
        public class TestModel1
        {
            public Guid Id { get; set; }
            public Guid ObjectId { get; set; }
            public int DefinitionId { get; set; }
            public int Stage { get; set; }
            public int Mode { get; set; }
        }

        [DocumentAlias("testmodel2")]
        public class TestModel2
        {
            public Guid Id { get; set; }
            public string Content { get; set; }
        }

        [DocumentAlias("testmodel3")]
        public class TestModel3
        {
            public Guid Id { get; set; }
            public Guid ObjectId { get; set; }
            public int DefinitionId { get; set; }
            public int Stage { get; set; }
            public int Cond1 { get; set; }
            public int Cond2 { get; set; }
        }

        [DocumentAlias("testmodel4")]
        public class TestModel4
        {
            public Guid Id { get; set; }
            public Guid ObjectId { get; set; }
            public int DefinitionId { get; set; }
            public bool Bool1 { get; set; }
            public bool Bool2 { get; set; }
            public bool Bool3 { get; set; }
            public string Fields1 { get; set; }
            public string Fields2 { get; set; }
            public string Attr { get; set; }
            public bool Bool5 { get; set; }
            public int Stages { get; set; }
            public string Text { get; set; }
            public bool Bool4 { get; set; }
            public string Mode { get; set; }
        }

        [DocumentAlias("testmodel5")]
        public class TestModel5
        {
            public Guid Id { get; set; }
            public Guid ObjectId { get; set; }
            public int DefinitionId { get; set; }
            public string Name { get; set; }
            public int Stage { get; set; }
        }

        [DocumentAlias("testmodel6")]
        public class TestModel6
        {
            public Guid Id { get; set; }
            public Guid ObjectId { get; set; }
            public int DefinitionId { get; set; }
            public int Stage { get; set; }
            public string Text { get; set; }
        }

    }
}
