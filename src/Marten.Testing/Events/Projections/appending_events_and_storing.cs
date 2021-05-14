using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Patching;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class appending_events_and_storing: IntegrationContext
    {
        [Theory]
        [InlineData(TenancyStyle.Single)]
        [InlineData(TenancyStyle.Conjoined)]
        public async Task patch_inside_inline_projection_does_not_error_during_savechanges(TenancyStyle tenancyStyle)
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TenancyStyle = tenancyStyle;

                _.Projections.Add(new QuestPatchTestProjection());
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var aggregateId = Guid.NewGuid();
            var quest = new Quest
            {
                Id = aggregateId,
            };
            var questStarted = new QuestStarted
            {
                Id = aggregateId,
                Name = "New Quest",
            };

            theSession.Events.Append(aggregateId, quest, questStarted);
            await theSession.SaveChangesAsync();

            (await theSession.Events.FetchStreamStateAsync(aggregateId)).Version.ShouldBe(2);
        }

        public class QuestPatchTestProjection: IProjection
        {
            public Guid Id { get; set; }

            public string Name { get; set; }

            public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
            {
                var questEvents = streams.SelectMany(x => x.Events).OrderBy(s => s.Sequence).Select(s => s.Data);

                foreach (var @event in questEvents)
                {
                    if (@event is Quest quest)
                    {
                        operations.Store(new QuestPatchTestProjection { Id = quest.Id });
                    }
                    else if (@event is QuestStarted started)
                    {
                        operations.Patch<QuestPatchTestProjection>(started.Id).Set(x => x.Name, "New Name");
                    }
                }
            }

            public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
                CancellationToken cancellation)
            {
                return Task.CompletedTask;
            }
        }

        public appending_events_and_storing(DefaultStoreFixture fixture) : base(fixture)
        {
            DocumentTracking = DocumentTracking.IdentityOnly;
        }
    }
}
