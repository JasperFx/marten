using System;
using System.Linq;
using System.Threading.Tasks;
using Bug1779;
using Marten.Testing.Harness;

using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Linq
{
    public class Bug_1779_null_comparison_to_foreign_key_column_not_generating_is_null : BugIntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_1779_null_comparison_to_foreign_key_column_not_generating_is_null(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task should_be_able_to_filter_with_null_value()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<HierarchyEntity>()
                    .ForeignKey<HierarchyEntity>(m => m.ParentId);
            });

            documentStore.Advanced.Clean.DeleteAllDocuments();

            await using var session = documentStore.OpenSession();
            session.Store(new HierarchyEntity {Name = "Test", ParentId = null});

            await session.SaveChangesAsync();

            await using var querySession = documentStore.QuerySession();
            querySession.Logger = new TestOutputMartenLogger(_output);

            var results = await querySession.Query<HierarchyEntity>()
                .Where(x => x.Name == "Test" && x.ParentId == null)
                .ToListAsync();

            results.Count.ShouldBe(1);
        }

        [Fact]
        public async Task should_be_able_to_filter_with_null_value_and_not_equals()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<HierarchyEntity>()
                    .ForeignKey<HierarchyEntity>(m => m.ParentId);
            });

            documentStore.Advanced.Clean.DeleteAllDocuments();

            var parentId = Guid.NewGuid();

            await using var session = documentStore.OpenSession();
            session.Store(new HierarchyEntity {Id = parentId, Name = "Parent"}, new HierarchyEntity {Name = "Test", ParentId = parentId});

            await session.SaveChangesAsync();

            await using var querySession = documentStore.QuerySession();
            querySession.Logger = new TestOutputMartenLogger(_output);

            var results = await querySession.Query<HierarchyEntity>()
                .Where(x => x.Name == "Test" && x.ParentId != null)
                .ToListAsync();

            results.Count.ShouldBe(1);
        }
    }
}


namespace Bug1779
{
    public class HierarchyEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
        public Guid? ParentId { get; set; }
    }
}
