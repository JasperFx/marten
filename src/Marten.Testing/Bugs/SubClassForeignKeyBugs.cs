using Xunit;

namespace Marten.Testing.Bugs
{
    public class SubClassForeignKeyBugs: IntegratedFixture
    {
        public class Person
        {
            public int Id { get; set; }
            public int DepartmentId { get; set; }
        }

        public class Employee: Person
        {
        }

        public class Address
        {
            public int Id { get; set; }
            public int ParentId { get; set; }
        }

        public class Department
        {
            public int Id { get; set; }
        }

        public SubClassForeignKeyBugs()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Person>()
                    .AddSubClass<Employee>()
                    .ForeignKey<Department>(p => p.DepartmentId);

                _.Schema.For<Address>()
                    .ForeignKey<Person>(c => c.ParentId);
            });
        }

        [Fact]
        public void ForeignKeyEntitiesToSubClassesShouldBeInsertedFirst()
        {
            using (var session = theStore.OpenSession())
            {
                var department = new Department { Id = 37 };
                session.Store(department);
                session.SaveChanges();
            }
            using (var session = theStore.OpenSession())
            {
                // Creating an employee with an address - the employee object should be
                // inserted *before* the address object.
                var employee = new Employee { Id = 222, DepartmentId = 37 };
                session.Store(employee);
                var address = new Address { ParentId = 222, Id = 42 };
                session.Store(address);

                session.SaveChanges();
            }
        }

        [Fact]
        public void ForeignKeysOnSubClassesShouldInsertedFirst()
        {
            using (var session = theStore.OpenSession())
            {
                var department = new Department { Id = 1 };
                session.Store(department);

                var employee = new Employee { Id = 2, DepartmentId = 1 };
                session.Store(employee);

                session.SaveChanges();
            }
        }
    }
}
