using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Vogen;
using Shouldly;

namespace ValueTypeTests.Bugs;

public class querying_by_value_types : BugIntegrationContext
{
    [Fact]
    public async Task run_queries()
    {
        StoreOptions(opts =>
        {
            opts.RegisterValueType(typeof(EmailAddress));
            opts.RegisterValueType(typeof(Age));
        });

        var customer = new Customer
        {
            Email = EmailAddress.From("example@me.com"),
            Age = Age.From(25)
        };

        theSession.Store(customer);

        await theSession.SaveChangesAsync();

        var loadedCustomer = await theSession.LoadAsync<Customer>(customer.Id);

        loadedCustomer.Email.ShouldNotBeNull();
        loadedCustomer.Email.Value.ShouldBe("example@me.com");


        var queryByAge = await theSession.Query<Customer>()
            .FirstOrDefaultAsync(x => x.Age == 25);

        queryByAge.ShouldNotBeNull();

        var queryByEmail = await theSession.Query<Customer>()
            .FirstOrDefaultAsync(x => x.Email == customer.Email);

        queryByEmail.ShouldNotBeNull();
    }
}

[ValueObject<string>]
public partial record EmailAddress;

[ValueObject<int>]
public partial record Age;

public class Customer
{
    public Guid Id { get; set; }

    public required EmailAddress Email { get; init; }

    public required Age Age { get; init; }
}
