using Marten.Services;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class SubClassForeignKeyBugs : IntegratedFixture
    {
        public class Person
        {
            public int Id { get; set; }
        }

        public class Employee : Person
        {
        }

        public class Address
        {
            public int Id { get; set; }
            public int ParentId { get; set; }
        }
        
        public SubClassForeignKeyBugs()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Person>()
                    .AddSubClass<Employee>();

                _.Schema.For<Address>()
                    .ForeignKey<Person>(c => c.ParentId);
            });
        }

        [Fact]
        public void ForeignKeysOnDerivedClassesShouldBeInsertedFirst()
        {
            using (var session = theStore.OpenSession())
            {
                // Creating an employee with an address - the employee object should be
                // inserted *before* the address object.
                var employee = new Employee {Id = 222};
                session.Store(employee);
                var address = new Address {ParentId = 222, Id = 42};
                session.Store(address);

                session.SaveChanges();
            }
        }
    }
}