using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Pagination;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_3351_Include_with_ToPagedListAsync : BugIntegrationContext
{
    [Fact]
    public async Task should_work_just_fine()
    {
        theSession.Store(new UserInformation3351
        {
            Id = "hansolo", Company = "Acme"
        });

        theSession.Store(new User3351
        {
            Id = "hansolo", FirstName = "Han"
        });

        await theSession.SaveChangesAsync();

        var userInfo = new Dictionary<string, UserInformation3351>();
        var users = await theSession
            .Query<User3351>()
            .Include(x => x.Id, userInfo)
            .ToPagedListAsync(1, 1); // This does not

        users.Single().Id.ShouldBe("hansolo");
        userInfo["hansolo"].Company.ShouldBe("Acme");
    }
}

public class User3351
{
    public string? Id { get; set; }
    public string? FirstName { get; set; }
}

public class UserInformation3351
{
    public string? Id { get; set; }
    public string? Company { get; set; }
}
