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
        private PatchExpression<Target> expression;
        private IDocumentSchema schema = Substitute.For<IDocumentSchema>();
        

        public PatchExpressionTests()
        {
            expression = new PatchExpression<Target>(null, schema, new UnitOfWork(schema));
        }

        [Fact]
        public void builds_patch_for_set_shallow()
        {
            expression.Set(x => x.Number, 5);

            expression.Patch["path"].ShouldBe("Number");
            expression.Patch["type"].ShouldBe("set");
            expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void builds_patch_for_set_2_deep()
        {
            expression.Set(x => x.Inner.Number, 5);

            expression.Patch["path"].ShouldBe("Inner.Number");
            expression.Patch["type"].ShouldBe("set");
            expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void builds_patch_for_set_3_deep()
        {
            expression.Set(x => x.Inner.Inner.Number, 5);

            expression.Patch["path"].ShouldBe("Inner.Inner.Number");
            expression.Patch["type"].ShouldBe("set");
            expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void increment_int_with_default()
        {
            expression.Increment(x => x.Number);

            expression.Patch["path"].ShouldBe("Number");
            expression.Patch["type"].ShouldBe("increment");
            expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_int_with_default_deep()
        {
            expression.Increment(x => x.Inner.Inner.Number);

            expression.Patch["path"].ShouldBe("Inner.Inner.Number");
            expression.Patch["type"].ShouldBe("increment");
            expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_int_with_explicit_interval()
        {
            expression.Increment(x => x.Number, 5);

            expression.Patch["path"].ShouldBe("Number");
            expression.Patch["type"].ShouldBe("increment");
            expression.Patch["increment"].ShouldBe(5);
        }

        [Fact]
        public void increment_long_with_default()
        {
            expression.Increment(x => x.Long);

            expression.Patch["path"].ShouldBe("Long");
            expression.Patch["type"].ShouldBe("increment");
            expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_long_with_default_deep()
        {
            expression.Increment(x => x.Inner.Inner.Long);

            expression.Patch["path"].ShouldBe("Inner.Inner.Long");
            expression.Patch["type"].ShouldBe("increment");
            expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_long_with_explicit_interval()
        {
            expression.Increment(x => x.Long, 5);

            expression.Patch["path"].ShouldBe("Long");
            expression.Patch["type"].ShouldBe("increment");
            expression.Patch["increment"].ShouldBe(5);
        }

        [Fact]
        public void increment_double_with_default()
        {
            expression.Increment(x => x.Double);

            expression.Patch["path"].ShouldBe("Double");
            expression.Patch["type"].ShouldBe("increment_float");
            expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_double_with_default_deep()
        {
            expression.Increment(x => x.Inner.Inner.Double);

            expression.Patch["path"].ShouldBe("Inner.Inner.Double");
            expression.Patch["type"].ShouldBe("increment_float");
            expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_double_with_explicit_interval()
        {
            expression.Increment(x => x.Double, 5);

            expression.Patch["path"].ShouldBe("Double");
            expression.Patch["type"].ShouldBe("increment_float");
            expression.Patch["increment"].ShouldBe(5);
        }

        [Fact]
        public void increment_float_with_default()
        {
            expression.Increment(x => x.Float);

            expression.Patch["path"].ShouldBe("Float");
            expression.Patch["type"].ShouldBe("increment_float");
            expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_float_with_default_deep()
        {
            expression.Increment(x => x.Inner.Inner.Float);

            expression.Patch["path"].ShouldBe("Inner.Inner.Float");
            expression.Patch["type"].ShouldBe("increment_float");
            expression.Patch["increment"].ShouldBe(1);
        }

        [Fact]
        public void increment_float_with_explicit_interval()
        {
            expression.Increment(x => x.Float, 5);

            expression.Patch["path"].ShouldBe("Float");
            expression.Patch["type"].ShouldBe("increment_float");
            expression.Patch["increment"].ShouldBe(5);
        }

        [Fact]
        public void append_shallow()
        {
            expression.Append(x => x.NumberArray, 5);

            expression.Patch["path"].ShouldBe("NumberArray");
            expression.Patch["type"].ShouldBe("append");
            expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void append_deep()
        {
            expression.Append(x => x.Inner.Inner.NumberArray, 5);

            expression.Patch["path"].ShouldBe("Inner.Inner.NumberArray");
            expression.Patch["type"].ShouldBe("append");
            expression.Patch["value"].ShouldBe(5);
        }

        [Fact]
        public void insert_shallow()
        {
            expression.Insert(x => x.NumberArray, 5);

            expression.Patch["path"].ShouldBe("NumberArray");
            expression.Patch["type"].ShouldBe("insert");
            expression.Patch["value"].ShouldBe(5);
            expression.Patch["index"].ShouldBe(0);
        }

        [Fact]
        public void insert_deep()
        {
            expression.Insert(x => x.Inner.Inner.NumberArray, 5);

            expression.Patch["path"].ShouldBe("Inner.Inner.NumberArray");
            expression.Patch["type"].ShouldBe("insert");
            expression.Patch["value"].ShouldBe(5);
            expression.Patch["index"].ShouldBe(0);
        }


        [Fact]
        public void insert_at_a_nonzero_index()
        {
            expression.Insert(x => x.NumberArray, 5, 2);

            expression.Patch["path"].ShouldBe("NumberArray");
            expression.Patch["type"].ShouldBe("insert");
            expression.Patch["value"].ShouldBe(5);
            expression.Patch["index"].ShouldBe(2);
        }

        [Fact]
        public void rename_shallow()
        {
            expression.Rename("Old", x => x.Double);

            expression.Patch["type"].ShouldBe("rename");
            expression.Patch["to"].ShouldBe("Double");
            expression.Patch["path"].ShouldBe("Old");
        }

        [Fact]
        public void rename_2_deep()
        {
            expression.Rename("Old", x => x.Inner.Double);

            expression.Patch["type"].ShouldBe("rename");
            expression.Patch["to"].ShouldBe("Double");
            expression.Patch["path"].ShouldBe("Inner.Old");
        }

        [Fact]
        public void rename_3_deep()
        {
            expression.Rename("Old", x => x.Inner.Inner.Double);

            expression.Patch["type"].ShouldBe("rename");
            expression.Patch["to"].ShouldBe("Double");
            expression.Patch["path"].ShouldBe("Inner.Inner.Old");
        }

        [Fact]
        public void remove_first()
        {
            expression.Remove(x => x.NumberArray, 5);

            expression.Patch["type"].ShouldBe("remove");
            expression.Patch["value"].ShouldBe(5);
            expression.Patch["path"].ShouldBe("NumberArray");
            expression.Patch["action"].ShouldBe((int)RemoveAction.RemoveFirst);
        }

        [Fact]
        public void remove_all()
        {
            expression.Remove(x => x.NumberArray, 5, RemoveAction.RemoveAll);

            expression.Patch["type"].ShouldBe("remove");
            expression.Patch["value"].ShouldBe(5);
            expression.Patch["path"].ShouldBe("NumberArray");
            expression.Patch["action"].ShouldBe((int) RemoveAction.RemoveAll);
        }
    }
}