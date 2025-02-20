using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Loading;

public class document_session_load_document: OneOffConfigurationsContext
{


    [Fact]
    public async Task when_collectionstorage_asarray_and_with_readonlycollection_with_integers_and_private_setter()
    {
        var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(1.Minutes());

        // CancellationToken
        var token = cancellation.Token;


        StoreOptions(opts =>
        {
            opts.UseNewtonsoftForSerialization(collectionStorage: CollectionStorage.AsArray);

        });

        var user = new UserWithReadonlyCollectionWithPrivateSetter(Guid.NewGuid(), "James", new[] { 1, 2, 3 });

        theSession.Store(user);
        await theSession.SaveChangesAsync(token);

        var userFromDb = await theSession.LoadAsync<UserWithReadonlyCollectionWithPrivateSetter>(user.Id, token);
        userFromDb.Id.ShouldBe(user.Id);
        userFromDb.Name.ShouldBe(user.Name);
        userFromDb.Collection.ShouldHaveTheSameElementsAs(user.Collection);
    }

    public document_session_load_document()
    {
    }
}
