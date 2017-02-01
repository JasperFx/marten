using System;
using Marten.Patching;
using Marten.Schema;
using Marten.Services;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Patching
{
    public class PatchExpressionTests
    {
        private readonly PatchExpression<Target> _expression;
        private readonly IDocumentSchema _schema = Substitute.For<IDocumentSchema>();
        

        public PatchExpressionTests()
        {
            var queryable = Substitute.For<IQueryableDocument>();
            queryable.DocumentType.Returns(typeof(Target));

            var mapping = Substitute.For<IDocumentMapping>();
            mapping.ToQueryableDocument().Returns(queryable);

            _schema.MappingFor(typeof(Target)).Returns(mapping);

            _expression = new PatchExpression<Target>(null, _schema, new UnitOfWork(_schema), new JsonNetSerializer());
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
            Assert.Throws<ArgumentException>(() => _expression.Duplicate(x => x.String))
                .Message.ShouldContain("At least one destination must be given");
        }
    }
}