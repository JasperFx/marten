using System;
using System.Data;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace StressTests.Bugs;

public class Bug616Account
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }

    public void Substract(int value)
    {
        Amount = Amount - value;
    }
}

public class Bug_616_not_possible_to_use_Serializable_transactions: IntegrationContext
{
    [Fact]
    public async Task conccurent_write_should_throw_an_exception()
    {
        var accountA = new Bug616Account { Id = Guid.NewGuid(), Amount = 100 };
        theSession.Store(accountA);
        await theSession.SaveChangesAsync();

        using (var session1 = theStore.DirtyTrackedSession(IsolationLevel.Serializable))
        using (var session2 = theStore.DirtyTrackedSession(IsolationLevel.Serializable))
        {
            var session1AcountA = session1.Load<Bug616Account>(accountA.Id);
            session1AcountA.Substract(500);

            var session2AcountA = session2.Load<Bug616Account>(accountA.Id);
            session2AcountA.Substract(350);

            await session1.SaveChangesAsync();

            await Should.ThrowAsync<ConcurrentUpdateException>(async () =>
            {
                await session2.SaveChangesAsync();
            });
        }
    }

    public Bug_616_not_possible_to_use_Serializable_transactions(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
