using Marten.Internal;
using Marten.Linq.Fields;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Util;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.Internals
{
    public class DummyStatement: Statement
    {
        public DummyStatement() : base(Substitute.For<IFieldMapping>())
        {
        }

        protected override void configure(CommandBuilder builder)
        {
            throw new System.NotImplementedException();
        }
    }


    public class StatementTests
    {
        [Fact]
        public void appending_child_converts_to_CTE()
        {
            var root = new DummyStatement();
            var descendent = new DummyStatement();

            root.InsertAfter(descendent);

            root.Next.ShouldBe(descendent);
            descendent.Previous.ShouldBe(root);

        }
    }

    public class when_inserting_a_statement_before_an_unattached_statement
    {
        private DummyStatement original;
        private DummyStatement newRoot;

        public when_inserting_a_statement_before_an_unattached_statement()
        {
            var session = Substitute.For<IMartenSession>();
            session.NextTempTableName().Returns("NextTempTable");

            original = new DummyStatement
            {
                Mode = StatementMode.Select
            };

            newRoot = new DummyStatement();


            original.InsertBefore(newRoot);
        }

        [Fact]
        public void relationships()
        {
            newRoot.Next.ShouldBe(original);
            original.Previous.ShouldBe(newRoot);
        }

        [Fact]
        public void new_root_is_top()
        {
            newRoot.Previous.ShouldBeNull();
            original.Top().ShouldBe(newRoot);
            newRoot.Top().ShouldBe(newRoot);
        }

        [Fact]
        public void original_is_current()
        {
            original.Current().ShouldBe(original);
            newRoot.Current().ShouldBe(original);
        }
    }

    public class when_inserting_statement_in_front_of_statement_that_is_not_the_top
    {
        private DummyStatement root = new DummyStatement();
        private DummyStatement original = new DummyStatement();
        private DummyStatement inserted = new DummyStatement();

        public when_inserting_statement_in_front_of_statement_that_is_not_the_top()
        {
            var session = Substitute.For<IMartenSession>();
            session.NextTempTableName().Returns("NextTempTable");

            root.InsertAfter(original);

            inserted = new DummyStatement();

            original.InsertBefore(inserted);
        }

        [Fact]
        public void relationships()
        {
            root.Next.ShouldBe(inserted);
            inserted.Previous.ShouldBe(root);
            inserted.Next.ShouldBe(original);
            original.Previous.ShouldBe(inserted);
        }

        [Fact]
        public void root_is_still_top()
        {
            original.Top().ShouldBe(root);
            inserted.Top().ShouldBe(root);
        }

        [Fact]
        public void original_is_current()
        {
            original.Current().ShouldBe(original);
            inserted.Current().ShouldBe(original);
        }


    }

}
