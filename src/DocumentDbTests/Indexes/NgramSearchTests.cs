using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Indexes;

public class NgramSearchTests : Marten.Testing.Harness.OneOffConfigurationsContext
{

    public sealed class Address
    {
        public Address(string line1, string line2)
        {
            Line1 = line1;
            Line2 = line2;
        }

        public string Line1 { get; set; }
        public string Line2 { get; set; }
    }
    public sealed class User
    {
        public int Id { get; set; }
        public string UserName { get; set; }

        public Address Address { get; set; }

        public User(int id, string userName, Address address=null)
        {
            Id = id;
            UserName = userName;
            Address = address;
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
        ShouldBeStringTestExtensions.ShouldContain(result[0].UserName, term);
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
        ShouldBeStringTestExtensions.ShouldContain(result[0].UserName, term);
    }

    [Fact]
    public async Task test_ngram_on_nested_prop_search_returns_data()
    {
        var store = DocumentStore.For(_ =>
        {
            _.Connection(Marten.Testing.Harness.ConnectionSource.ConnectionString);

            // This creates
            _.Schema.For<User>().NgramIndex(x => x.Address.Line1);
        });

        await using var session = store.LightweightSession();

        string term = null;
        for (var i = 1; i < 4; i++)
        {
            var guid = $"{Guid.NewGuid():N}";
            term ??= guid.Substring(5);
            var address = new Address(guid, guid);
            var newUser = new User(i, $"Test user {guid}", address);

            session.Store(newUser);
        }

        await session.SaveChangesAsync();

        #region sample_ngram_search
        var result = await session
            .Query<User>()
            .Where(x => x.Address.Line1.NgramSearch(term))
            .ToListAsync();
        #endregion

        result.ShouldNotBeNull();
        result.ShouldHaveSingleItem();
        ShouldBeStringTestExtensions.ShouldContain(result[0].Address.Line1, term);
    }
}
