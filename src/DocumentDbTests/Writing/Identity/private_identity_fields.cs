using System;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity;

public class private_identity_fields : IntegrationContext
{
    public private_identity_fields(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task when_id_setter_is_private()
    {
        var user = new UserWithPrivateId();

        theSession.Store(user);
        await theSession.SaveChangesAsync();

        user.Id.ShouldNotBe(Guid.Empty);

        var issue = await theSession.LoadAsync<Issue>(user.Id);
        issue.ShouldBeNull();
    }

    [Fact]
    public async Task when_no_id_setter()
    {
        var user = new UserWithoutIdSetter();

        theSession.Store(user);
        await theSession.SaveChangesAsync();

        user.Id.ShouldBe(Guid.Empty);
    }
}
