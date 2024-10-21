using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Patching;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace PatchingTests.Patching;

public class MultiTenancyFixture: StoreFixture
{
    public MultiTenancyFixture(): base("multi_tenancy")
    {
        Options.Policies.AllDocumentsAreMultiTenanted();
        Options.Schema.For<User>().UseOptimisticConcurrency(true);
    }
}

[Collection("multi_tenancy")]
public class multi_tenancy: StoreContext<MultiTenancyFixture>, IClassFixture<MultiTenancyFixture>, IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly Target[] _greens = Target.GenerateRandomData(100).ToArray();

    private readonly Target[] _reds = Target.GenerateRandomData(100).ToArray();
    private readonly Target[] blues = Target.GenerateRandomData(25).ToArray();
    private readonly Target targetBlue1 = Target.Random();
    private readonly Target targetBlue2 = Target.Random();
    private readonly Target targetRed1 = Target.Random();
    private readonly Target targetRed2 = Target.Random();

    public multi_tenancy(MultiTenancyFixture fixture, ITestOutputHelper output): base(fixture)
    {
        _output = output;

    }

    public async Task InitializeAsync()
    {
        using (var session = theStore.LightweightSession("Red"))
        {
            session.Store(targetRed1, targetRed2);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("Blue"))
        {
            session.Store(targetBlue1, targetBlue2);
            await session.SaveChangesAsync();
        }
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }


    [Fact]
    public async Task patching_respects_tenancy_too()
    {
        var user = new User { UserName = "Me", FirstName = "Jeremy", LastName = "Miller" };
        user.Id = Guid.NewGuid();

        using (var red = theStore.LightweightSession("Red"))
        {
            red.Store(user);
            await red.SaveChangesAsync();
        }

        using (var green = theStore.LightweightSession("Green"))
        {
            green.Patch<User>(user.Id).Set(x => x.FirstName, "John");
            await green.SaveChangesAsync();
        }

        using (var red = theStore.QuerySession("Red"))
        {
            var final = red.Load<User>(user.Id);
            final.FirstName.ShouldBe("Jeremy");
        }
    }

    [Fact]
    public async Task patching_respects_tenancy_too_2()
    {
        var user = new User { UserName = "Me", FirstName = "Jeremy", LastName = "Miller" };
        user.Id = Guid.NewGuid();

        using (var red = theStore.LightweightSession("Red"))
        {
            red.Store(user);
            await red.SaveChangesAsync();
        }

        using (var green = theStore.LightweightSession("Green"))
        {
            green.Patch<User>(x => x.UserName == "Me").Set(x => x.FirstName, "John");
            await green.SaveChangesAsync();
        }

        using (var red = theStore.QuerySession("Red"))
        {
            var final = red.Load<User>(user.Id);
            final.FirstName.ShouldBe("Jeremy");
        }
    }


    [MultiTenanted]
    public class TenantedDoc
    {
        public Guid Id;
    }
}
