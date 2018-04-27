using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Marten.Services;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class appending_events_and_storing : DocumentSessionFixture<IdentityMap>
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
                _.Events.InlineProjections.Add(new QuestPatchTestProjection());
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

        public class QuestPatchTestProjection : IProjection
        {
            public Guid Id { get; set; }

            public string Name { get; set; }

            public void Apply(IDocumentSession session, EventPage page)
            {
                var questEvents = page.Events.OrderBy(s => s.Sequence).Select(s => s.Data);

                foreach (var @event in questEvents)
                {
                    if (@event is Quest)
                    {
                        session.Store(new QuestPatchTestProjection { Id = ((Quest)@event).Id });
                    }
                    else if (@event is QuestStarted)
                    {
                        session.Patch<QuestPatchTestProjection>(((QuestStarted)@event).Id).Set(x => x.Name, "New Name");
                    }
                }
            }

            public Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token)
            {
                return Task.CompletedTask;
            }

            public void EnsureStorageExists(ITenant tenant)
            {
                tenant.EnsureStorageExists(typeof(QuestPatchTestProjection));
            }

            public Type[] Consumes { get; } = new Type[] { typeof(Quest), typeof(QuestStarted) };

            public Type Produces { get; } = typeof(QuestPatchTestProjection);

            public AsyncOptions AsyncOptions { get; } = new AsyncOptions();
        }
    }
}