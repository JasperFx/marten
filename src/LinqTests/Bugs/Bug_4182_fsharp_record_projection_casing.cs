using System;
using System.Linq;
using System.Threading.Tasks;
using static FSharpTypes;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

public class Bug_4182_fsharp_record_projection_casing : OneOffConfigurationsContext
{
    [Fact]
    public async Task GroupJoin_SelectMany_with_fsharp_record_projection_using_stj()
    {
        var store = StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization();
            opts.Schema.For<FSharpMembership>();
            opts.Schema.For<FSharpUser>();
        });

        await using var session = store.LightweightSession();

        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        session.Store(new FSharpUser(
            id: userId,
            firstName: "Alice",
            lastName: "Smith",
            email: "alice@example.com"
        ));

        session.Store(new FSharpMembership(
            id: Guid.NewGuid(),
            userId: userId,
            organizationId: orgId,
            role: "Admin",
            addedOn: DateTimeOffset.UtcNow
        ));

        await session.SaveChangesAsync();

        // This query projects into an F# record type (FSharpMemberDto).
        // F# records have camelCase constructor params but PascalCase properties.
        // Marten's SelectParser uses constructor param names for jsonb_build_object keys,
        // which causes STJ deserialization to fail because it expects PascalCase property names.
        var results = await session.Query<FSharpMembership>()
            .GroupJoin(
                session.Query<FSharpUser>(),
                m => m.UserId,
                u => u.Id,
                (m, users) => new { m, users })
            .SelectMany(
                x => x.users,
                (x, u) => new FSharpMemberDto(
                    x.m.UserId,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    x.m.Role,
                    x.m.AddedOn))
            .ToListAsync();

        results.Count.ShouldBe(1);
        var dto = results.First();
        dto.UserId.ShouldBe(userId);
        dto.FirstName.ShouldBe("Alice");
        dto.LastName.ShouldBe("Smith");
        dto.Email.ShouldBe("alice@example.com");
        dto.Role.ShouldBe("Admin");
    }

    [Fact]
    public async Task GroupJoin_SelectMany_with_fsharp_record_projection_using_newtonsoft()
    {
        var store = StoreOptions(opts =>
        {
            // Default Newtonsoft - should work
            opts.Schema.For<FSharpMembership>();
            opts.Schema.For<FSharpUser>();
        });

        await using var session = store.LightweightSession();

        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        session.Store(new FSharpUser(
            id: userId,
            firstName: "Alice",
            lastName: "Smith",
            email: "alice@example.com"
        ));

        session.Store(new FSharpMembership(
            id: Guid.NewGuid(),
            userId: userId,
            organizationId: orgId,
            role: "Admin",
            addedOn: DateTimeOffset.UtcNow
        ));

        await session.SaveChangesAsync();

        var results = await session.Query<FSharpMembership>()
            .GroupJoin(
                session.Query<FSharpUser>(),
                m => m.UserId,
                u => u.Id,
                (m, users) => new { m, users })
            .SelectMany(
                x => x.users,
                (x, u) => new FSharpMemberDto(
                    x.m.UserId,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    x.m.Role,
                    x.m.AddedOn))
            .ToListAsync();

        results.Count.ShouldBe(1);
        var dto = results.First();
        dto.UserId.ShouldBe(userId);
        dto.FirstName.ShouldBe("Alice");
        dto.LastName.ShouldBe("Smith");
        dto.Email.ShouldBe("alice@example.com");
        dto.Role.ShouldBe("Admin");
    }
}
