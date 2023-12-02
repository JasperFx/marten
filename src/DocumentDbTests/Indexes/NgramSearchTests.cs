using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Indexes;

public class NgramSearchTests : Marten.Testing.Harness.OneOffConfigurationsContext
{

    public sealed class User
    {
        public int Id { get; set; }
        public string UserName { get; set; }

        public User(int id, string userName)
        {
            Id = id;
            UserName = userName;
        }
    }

    [Fact]
    public async Task test_ngram_search_returns_data()
    {
        var store = DocumentStore.For(_ =>
        {
            _.Connection(Marten.Testing.Harness.ConnectionSource.ConnectionString);

            // This creates
            _.Schema.For<User>().NgramIndex(x => x.UserName);
        });

        await using var session = store.LightweightSession();

        string term = null;
        for (var i = 1; i < 4; i++)
        {
            var guid = $"{Guid.NewGuid():N}";
            term ??= guid.Substring(5);

            var newUser = new User(i, $"Test user {guid}");

            session.Store(newUser);
        }

        await session.SaveChangesAsync();

        #region sample_ngram_search
        var result = await session
            .Query<User>()
            .Where(x => x.UserName.NgramSearch(term))
            .ToListAsync();
        #endregion

        result.ShouldNotBeNull();
        result.ShouldHaveSingleItem();
        result[0].UserName.ShouldContain(term);
    }

    [Fact]
    public async Task test_ngram_search_returns_data_using_db_schema()
    {
        #region sample_ngram_search
        var store = DocumentStore.For(_ =>
        {
            _.Connection(Marten.Testing.Harness.ConnectionSource.ConnectionString);

            _.DatabaseSchemaName = "ngram_test";

            // This creates an ngram index for efficient sub string based matching
            _.Schema.For<User>().NgramIndex(x => x.UserName);
        });

        await using var session = store.LightweightSession();

        string term = null;
        for (var i = 1; i < 4; i++)
        {
            var guid = $"{Guid.NewGuid():N}";
            term ??= guid.Substring(5);

            var newUser = new User(i, $"Test user {guid}");

            session.Store(newUser);
        }

        await session.SaveChangesAsync();

        var result = await session
            .Query<User>()
            .Where(x => x.UserName.NgramSearch(term))
            .ToListAsync();
        #endregion

        result.ShouldNotBeNull();
        result.ShouldHaveSingleItem();
        result[0].UserName.ShouldContain(term);
    }
}
