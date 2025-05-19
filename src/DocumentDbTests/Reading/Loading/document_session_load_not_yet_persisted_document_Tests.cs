﻿using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Loading;

public class not_tracked_document_session_load_not_yet_persisted_document_Tests : IntegrationContext
{
    [Fact]
    public async Task then_a_new_document_should_be_returned()
    {
        var user1 = new User { FirstName = "Tim", LastName = "Cools" };

        theSession.Store(user1);

        var fromSession = await theSession.LoadAsync<User>(user1.Id);

        fromSession.ShouldNotBeSameAs(user1);
    }

    [Fact]
    public async Task given_document_is_already_added_then_a_new_document_should_be_returned()
    {
        var user1 = new User { FirstName = "Tim", LastName = "Cools" };

        theSession.Store(user1);
        theSession.Store(user1);

        var fromSession = await theSession.LoadAsync<User>(user1.Id);

        fromSession.ShouldNotBeSameAs(user1);
    }

    [Fact]
    public async Task given_document_with_same_id_is_already_added_then_exception_should_occur()
    {
        var user1 = new User { FirstName = "Tim", LastName = "Cools" };
        var user2 = new User { FirstName = "Tim2", LastName = "Cools2", Id = user1.Id };

        theSession.Store(user1);
        theSession.Store(user2);
        await theSession.SaveChangesAsync();

        //the non tracked session doesn't verify whether changer are already added.
        //so no exception should be thrown
    }

    public not_tracked_document_session_load_not_yet_persisted_document_Tests(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
