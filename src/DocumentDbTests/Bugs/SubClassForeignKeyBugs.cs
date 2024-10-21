using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class SubClassForeignKeyBugs: BugIntegrationContext
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
    public async Task ForeignKeyEntitiesToSubClassesShouldBeInsertedFirst()
    {
        using (var session = theStore.LightweightSession())
        {
            var department = new Department { Id = 37 };
            session.Store(department);
            await session.SaveChangesAsync();
        }
        using (var session = theStore.LightweightSession())
        {
            // Creating an employee with an address - the employee object should be
            // inserted *before* the address object.
            var employee = new Employee { Id = 222, DepartmentId = 37 };
            session.Store(employee);
            var address = new Address { ParentId = 222, Id = 42 };
            session.Store(address);

            await session.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ForeignKeysOnSubClassesShouldInsertedFirst()
    {
        using var session = theStore.LightweightSession();
        var department = new Department { Id = 1 };
        session.Store(department);

        var employee = new Employee { Id = 2, DepartmentId = 1 };
        session.Store(employee);

        await session.SaveChangesAsync();
    }
}
