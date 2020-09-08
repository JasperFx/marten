using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Bugs
{
    public class Bug_849_not_node_not_correctly_evaluated: IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public class TestClass
        {
            public TestClass()
            {
                Id = Guid.NewGuid();
            }

            public Guid Id { get; set; }
            public bool Flag { get; set; }
        }

        internal TestClass TestNullObject { get; set; }

        [Fact]
        public void When_Property_Is_Null_Exception_Should_Be_Null_Reference_Exception()
        {
            var flagFalse = new TestClass { Flag = false };
            var flagTrue = new TestClass { Flag = true };

            theSession.Store(flagFalse, flagTrue);
            theSession.SaveChanges();

            using (var s = theStore.OpenSession())
            {
                var items = s.Query<TestClass>().Where(x => !x.Flag == false).ToList();

                items.Single().Id.ShouldBe(flagTrue.Id);

            }
        }

        public Bug_849_not_node_not_correctly_evaluated(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
        }
    }
}
