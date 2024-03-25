using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;

namespace LinqTests.Bugs;

public class Bug_3082_trouble_with_repeated_include_usage : BugIntegrationContext
{
    [Fact]
    public async Task query_multiple_times()
    {
        await executeQuery();
        await executeQuery();
    }

    private async Task executeQuery()
    {
        User3082 user = null;
        BusinessFields businessField = null;
        var userEducation = await theSession
            .Query<UserEducation>()
            .Include<User3082>(x => x.UserId, u => user = u)
            .Include<BusinessFields>(x => x.EducationFieldId, bf => businessField = bf)
            .FirstOrDefaultAsync(x => x.Id == Guid.NewGuid().ToString());
    }
}

public class UserEducation
{
    public string Id{ get; set; }
    public string EducationFieldId { get; set; }
    public string UserId { get; set; }
}

public class User3082
{
    public string Id { get; set; }
    public string Name { get; set; }
}
public class BusinessFields
{
    public string Id { get; set; }
    public string Name { get; set; }
}
