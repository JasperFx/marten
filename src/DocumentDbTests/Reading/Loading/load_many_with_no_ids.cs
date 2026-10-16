using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.Internal.Sessions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Loading;

/// <summary>
///     <c>LoadManyAsync</c> with no ids has nothing to look up, so it must answer an empty list without going to the
///     database: an <c>id = ANY($1)</c> query over an empty array is a round trip that can only return no rows.
///     Callers build the id list from other data (the children a parent names, the rows of a page), and an empty list
///     is ordinary there.
/// </summary>
public class load_many_with_no_ids: IntegrationContext
{
    public enum SessionKind
    {
        Query,
        Lightweight,
        IdentityMap,
        DirtyTracked
    }

    private IQuerySession openSession(SessionKind kind) => kind switch
    {
        SessionKind.Query => theStore.QuerySession(),
        SessionKind.Lightweight => theStore.LightweightSession(),
        SessionKind.IdentityMap => theStore.IdentitySession(),
        SessionKind.DirtyTracked => theStore.DirtyTrackedSession(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [InlineData(SessionKind.Query)]
    [InlineData(SessionKind.Lightweight)]
    [InlineData(SessionKind.IdentityMap)]
    [InlineData(SessionKind.DirtyTracked)]
    public async Task no_ids_send_no_command(SessionKind kind)
    {
        await using var session = openSession(kind);
        var requests = ((QuerySession)session).RequestCount;

        (await session.LoadManyAsync<Target>(Array.Empty<Guid>())).ShouldBeEmpty();
        (await session.LoadManyAsync<Target>(new List<Guid>())).ShouldBeEmpty();
        (await session.LoadManyAsync<StringDoc>(Array.Empty<string>())).ShouldBeEmpty();
        (await session.LoadManyAsync<IntDoc>(Array.Empty<int>())).ShouldBeEmpty();
        (await session.LoadManyAsync<LongDoc>(Array.Empty<long>())).ShouldBeEmpty();

        ((QuerySession)session).RequestCount.ShouldBe(requests);
    }

    [Theory]
    [InlineData(SessionKind.Query)]
    [InlineData(SessionKind.Lightweight)]
    [InlineData(SessionKind.IdentityMap)]
    [InlineData(SessionKind.DirtyTracked)]
    public async Task ids_still_load_their_documents(SessionKind kind)
    {
        var target = Target.Random();
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        await using var session = openSession(kind);
        var loaded = await session.LoadManyAsync<Target>(target.Id);

        loaded.ShouldHaveSingleItem().Id.ShouldBe(target.Id);
    }

    public load_many_with_no_ids(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
