using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs
{
    public class Bug_1872_concurrent_creation_of_document_mappings
    {
        [Fact]
        public async Task try_to_make_it_crash()
        {
            using var store = DocumentStore.For(options =>
            {
                options.Connection(ConnectionSource.ConnectionString);
                options.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId);
            });

            var tasks = new List<Task>();
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    store.StorageFeatures.Build(typeof(Issue), store.Options);
                    store.StorageFeatures.Build(typeof(User), store.Options);
                    store.StorageFeatures.Build(typeof(Target), store.Options);
                }));
            }

            await Task.WhenAll(tasks);
        }
    }
}
