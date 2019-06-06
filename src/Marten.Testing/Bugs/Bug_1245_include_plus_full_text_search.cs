using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1245_include_plus_full_text_search: IntegratedFixture
    {
        private readonly bool _hasRequiredMinimumPgVersion;
        private readonly string _skipReason;

        public Bug_1245_include_plus_full_text_search()
        {
            var requiredMinimumPgVersion = Version.Parse("10.0");
            _hasRequiredMinimumPgVersion =
                theStore.Diagnostics.GetPostgresVersion().CompareTo(requiredMinimumPgVersion) >= 0;
            _skipReason = $"Test skipped, minimum Postgres version required is {requiredMinimumPgVersion}";
        }

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

        public sealed class User
        {
            public Guid Id { get; private set; }
            public string Name { get; private set; }

            public User(Guid id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        [SkippableFact]
        public void can_do_include_with_full_text_search()
        {
            Skip.IfNot(_hasRequiredMinimumPgVersion, _skipReason);

            var term = "content";
            var userDictionary = new Dictionary<Guid, User>();
            using (var session = theStore.OpenSession())
            {
                for( var i = 0; i < 3; i++)
                {
                    var newUser = new User(Guid.NewGuid(), $"Test user {i}");
                    var newEmail = new Email(Guid.NewGuid(), newUser.Id, $"Some content {i} {newUser.Name} ");

                    session.Store(newUser);
                    session.Store(newEmail);
                }

                session.SaveChanges();

                var query = session.Query<Email>()
                    .Include(x => x.UserId, userDictionary)
                    //.Where(x => x.Content.PlainTextSearch(term)).ToList();
                    //.Where(x => x.Content.Search(term)).ToList();
                    .Where(x => x.Content.PhraseSearch(term)).ToList();

                query.ShouldNotBeNull();
            }

        }

        [SkippableFact]
        public async Task can_do_include_with_full_text_search_async()
        {
            Skip.IfNot(_hasRequiredMinimumPgVersion, _skipReason);

            var term = "content";
            var userDictionary = new Dictionary<Guid, User>();
            using (var session = theStore.OpenSession())
            {
                for( var i = 0; i < 3; i++)
                {
                    var newUser = new User(Guid.NewGuid(), $"Test user {i}");
                    var newEmail = new Email(Guid.NewGuid(), newUser.Id, $"Some content {i} {newUser.Name} ");

                    session.Store(newUser);
                    session.Store(newEmail);
                }

                session.SaveChanges();

                var query = await session.Query<Email>()
                    .Include(x => x.UserId, userDictionary)
                    //.Where(x => x.Content.PlainTextSearch(term)).ToList();
                    //.Where(x => x.Content.Search(term)).ToList();
                    .Where(x => x.Content.PhraseSearch(term)).ToListAsync();

                query.ShouldNotBeNull();
            }

        }

    }

}
