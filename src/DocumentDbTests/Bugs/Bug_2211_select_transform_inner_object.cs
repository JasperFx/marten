using System;
using System.Linq;
using System.Threading.Tasks;
using Bug2211;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace DocumentDbTests.Bugs
{
    public class Bug_2211_select_transform_inner_object: BugIntegrationContext
    {
        [Fact]
        public async Task should_be_able_to_select_nested_objects()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.AutoCreateSchemaObjects = AutoCreate.All;
                x.Schema.For<TestEntity>();
            });

            await documentStore.Advanced.Clean.DeleteAllDocumentsAsync();

            await using var session = documentStore.OpenSession();
            var testEntity = new TestEntity { Name = "Test", Inner = new TestDto { Name = "TestDto" } };
            session.Store(testEntity);

            await session.SaveChangesAsync();

            await using var querySession = documentStore.QuerySession();

            var results = await querySession.Query<TestEntity>()
                .Select(x => new { Inner = x.Inner })
                .ToListAsync();

            results.Count.ShouldBe(1);
            results[0].Inner.Name.ShouldBe(testEntity.Inner.Name);
        }
    }
}

namespace Bug2211
{
    public class TestEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
        public TestDto Inner { get; set; }
    }

    public class TestDto
    {
        public string Name { get; set; }
    }
}
