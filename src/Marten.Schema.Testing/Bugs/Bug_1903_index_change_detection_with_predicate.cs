using System;
using System.Threading.Tasks;
using Marten.Testing.Linq;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Schema.Testing.Bugs
{
    public class Bug_1903_index_change_detection_with_predicate : IntegrationContext
    {
        [Fact]
        public async Task detect_delta()
        {
            StoreOptions(opts =>
            {
                opts.Schema.For<Student>()
                    .Duplicate(x => x.PhotoId)
                    .Index(x => x.PhotoId, index =>
                    {
                        index.IsUnique = true;
                        index.Predicate = "photo_id is not null";
                    });
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate);
            await theStore.Schema.AssertDatabaseMatchesConfigurationAsync();
        }
    }

    public class Student
    {
        public Guid Id { get; set; }
        public int? PhotoId { get; set; }
    }
}
