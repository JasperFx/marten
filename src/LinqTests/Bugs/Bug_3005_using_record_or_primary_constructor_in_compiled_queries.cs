using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_3005_using_record_or_primary_constructor_in_compiled_queries : BugIntegrationContext
{
    [Fact]
    public async Task warn_the_user_about_the_usability()
    {
        var target = Target.Random();
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        await Should.ThrowAsync<InvalidCompiledQueryException>(async () =>
        {
            var answer = await theSession.QueryAsync(new QueryWithPrimaryConstructor(target.Id));
        });



    }
}


public class QueryWithPrimaryConstructor(Guid Id) : ICompiledQuery<Target, Target> {

    public Expression<Func<IMartenQueryable<Target>, Target>> QueryIs() =>
        t => t.SingleOrDefault(t => t.Id == Id);

}
