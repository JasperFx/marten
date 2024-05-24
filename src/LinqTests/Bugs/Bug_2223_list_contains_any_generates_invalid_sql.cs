using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Bugs;

public class Bug_2223_list_contains_any_with_include_generates_invalid_sql: BugIntegrationContext
{
    [Fact]
    public async Task should_be_able_to_query_with_multiple_list_items_and_have_include()
    {
        using var documentStore = SeparateStore(x =>
        {
            x.AutoCreateSchemaObjects = AutoCreate.All;
            x.Schema.For<TestEntity>();
            x.Schema.For<OtherTestEntity>();
        });

        await documentStore.Advanced.Clean.DeleteAllDocumentsAsync();

        var otherEntityTestId = Guid.NewGuid();
        await using (var session = documentStore.LightweightSession())
        {
            var otherEntityOne = CreateOtherTestEntity(session, otherEntityTestId, "Other one");
            var otherEntityTwo = CreateOtherTestEntity(session, Guid.NewGuid(), "Other two");
            var otherEntityThree = CreateOtherTestEntity(session, Guid.NewGuid(), "Other three");

            session.Store(new TestEntity
            {
                Name = "Test",
                OtherIds = new List<Guid>
                {
                    otherEntityOne.Id,
                    otherEntityTwo.Id
                }
            });

            session.Store(new TestEntity
            {
                Name = "Test 2",
                OtherIds = new List<Guid>
                {
                    otherEntityTwo.Id,
                    otherEntityThree.Id
                }
            });

            await session.SaveChangesAsync();
        }

        await using (var session = documentStore.QuerySession())
        {
            var otherIdsQuery = new[]
            {
                otherEntityTestId,
                Guid.NewGuid()
            };

            var otherTestEntityLookup = new Dictionary<Guid, OtherTestEntity>();
            var entities = await session.Query<TestEntity>()
                .Include(x => x.OtherIds, otherTestEntityLookup)
                .Where(x => x.OtherIds.Any<Guid>(id => otherIdsQuery.Contains(id)))
                .ToListAsync();

            entities.Count.ShouldBe(1);
            entities[0].OtherIds.Count.ShouldBe(2);
            entities[0].OtherIds.ShouldContain(otherEntityTestId);

            otherTestEntityLookup.Count.ShouldBe(2);
            otherTestEntityLookup.ShouldContainKey(otherEntityTestId);
        }
    }

    private static OtherTestEntity CreateOtherTestEntity(IDocumentSession session, Guid id, string name)
    {
        var entity = new OtherTestEntity
        {
            Id = id,
            Name = name
        };

        session.Store(entity);
        return entity;
    }
}



public class OtherTestEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}
