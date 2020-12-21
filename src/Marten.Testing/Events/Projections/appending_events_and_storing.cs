using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.V4Concept;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class appending_events_and_storing: IntegrationContext
    {
        [Theory]
        [InlineData(TenancyStyle.Single)]
        [InlineData(TenancyStyle.Conjoined)]
        public void patch_inside_inline_projection_does_not_error_during_savechanges(TenancyStyle tenancyStyle)
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TenancyStyle = tenancyStyle;

                _.Events.V4Projections.Inline(new QuestPatchTestProjection());
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

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
            theSession.SaveChanges();

            theSession.Events.FetchStreamState(aggregateId).Version.ShouldBe(2);
        }

        public class QuestPatchTestProjection: IInlineProjection
        {
            public Guid Id { get; set; }

            public string Name { get; set; }

            public void Apply(IDocumentSession session, IReadOnlyList<StreamAction> streams)
            {
                var questEvents = streams.SelectMany(x => x.Events).OrderBy(s => s.Sequence).Select(s => s.Data);

                foreach (var @event in questEvents)
                {
                    if (@event is Quest quest)
                    {
                        session.Store(new QuestPatchTestProjection { Id = quest.Id });
                    }
                    else if (@event is QuestStarted started)
                    {
                        session.Patch<QuestPatchTestProjection>(started.Id).Set(x => x.Name, "New Name");
                    }
                }
            }

            public Task ApplyAsync(IDocumentSession session, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
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
