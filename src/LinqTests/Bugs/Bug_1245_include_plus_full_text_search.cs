using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_1245_include_plus_full_text_search: BugIntegrationContext
{
    public sealed class Email
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Content { get; set; }

        public Email(Guid id, Guid userId, string content)
        {
            Id = id;
            UserId = userId;
            Content = content;
        }
    }

    public sealed class Bug1245User
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; }

        public Bug1245User(Guid id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task can_do_include_with_full_text_search()
    {
        var term = "content";
        var userDictionary = new Dictionary<Guid, Bug1245User>();
        using var session = theStore.LightweightSession();
        for (var i = 0; i < 3; i++)
        {
            var newUser = new Bug1245User(Guid.NewGuid(), $"Test user {i}");
            var newEmail = new Email(Guid.NewGuid(), newUser.Id, $"Some content {i} {newUser.Name} ");

            session.Store(newUser);
            session.Store(newEmail);
        }

        await session.SaveChangesAsync();

        var query = session.Query<Email>()
            .Include(x => x.UserId, userDictionary)
            //.Where(x => x.Content.PlainTextSearch(term)).ToList();
            //.Where(x => x.Content.Search(term)).ToList();
            .Where(x => x.Content.PhraseSearch(term)).ToList();

        query.ShouldNotBeNull();
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task can_do_include_with_full_text_search_async()
    {
        var term = "content";
        var userDictionary = new Dictionary<Guid, Bug1245User>();
        await using var session = theStore.LightweightSession();
        for (var i = 0; i < 3; i++)
        {
            var newUser = new Bug1245User(Guid.NewGuid(), $"Test user {i}");
            var newEmail = new Email(Guid.NewGuid(), newUser.Id, $"Some content {i} {newUser.Name} ");

            session.Store(newUser);
            session.Store(newEmail);
        }

        await session.SaveChangesAsync();

        var query = await session.Query<Email>()
            .Include(x => x.UserId, userDictionary)
            //.Where(x => x.Content.PlainTextSearch(term)).ToList();
            //.Where(x => x.Content.Search(term)).ToList();
            .Where(x => x.Content.PhraseSearch(term)).ToListAsync();

        query.ShouldNotBeNull();
    }

}
