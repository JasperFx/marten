using System;
using System.Linq;
using Marten.Linq;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_849_not_node_not_correctly_evaluated : DocumentSessionFixture<NulloIdentityMap>
    {
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
	        var flagTrue = new TestClass { Flag = true } ;

			theSession.Store(flagFalse, flagTrue);
			theSession.SaveChanges();

	        using (var s = theStore.OpenSession())
	        {
		        var items = s.Query<TestClass>().Where(x => !x.Flag == false).ToList();
				
				Assert.Equal(1, items.Count);
		        Assert.Equal(flagTrue.Id, items[0].Id);
			}
		}
    }
}