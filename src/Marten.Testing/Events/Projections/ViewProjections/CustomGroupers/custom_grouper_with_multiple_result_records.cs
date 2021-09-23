#if NET
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections.ViewProjections.CustomGroupers
{
    public static class DateTimeExtensionMethods
    {
        public static DateTime ToStartOfMonth(this DateTime date)
        {
            return new DateTime(date.Year, date.Month, 1);
        }

        public static DateTime ToEndOfMonth(this DateTime date)
        {
            return date.ToStartOfMonth().AddMonths(1).AddSeconds(-1);
        }
    }

    #region sample_view-custom-grouper-with-multiple-result-records

    public record Allocation(DateTime Day, double Hours);

    public record EmployeeAllocated(Guid EmployeeId, List<Allocation> Allocations);

    public record EmployeeAllocatedInMonth(Guid EmployeeId, DateTime Month, List<Allocation> Allocations);

    public class MonthlyAllocation
    {
        public string Id { get; set; }

        public Guid EmployeeId { get; set; }
        public DateTime Month { get; set; }
        public double Hours { get; set; }
    }

    public class MonthlyAllocationProjection: ViewProjection<MonthlyAllocation, string>
    {
        public MonthlyAllocationProjection()
        {
            CustomGrouping(new MonthlyAllocationGrouper());
        }

        public void Apply(MonthlyAllocation allocation, EmployeeAllocated @event)
        {
            // Do nothing, this won't be triggered, it's just to satisify Marten selection logic
            throw new NotImplementedException();
        }

        public void Apply(MonthlyAllocation allocation, EmployeeAllocatedInMonth @event)
        {
            allocation.EmployeeId = @event.EmployeeId;
            allocation.Month = @event.Month;

            var hours = @event
                .Allocations
                .Sum(x => x.Hours);

            allocation.Hours += hours;
        }
    }

    public class MonthlyAllocationGrouper: IAggregateGrouper<string>
    {
        public Task Group(IQuerySession session, IEnumerable<IEvent> events, ITenantSliceGroup<string> grouping)
        {
            var monthlyEvents = events.OfType<IEvent<EmployeeAllocated>>()
                .SelectMany(@event =>
                    @event.Data.Allocations.Select(allocation => new
                    {
                        Allocation = allocation,
                        @event.Data.EmployeeId,
                        Month = allocation.Day.ToStartOfMonth(),
                        Event = @event
                    })
                )
                .GroupBy(ks => new { ks.EmployeeId, ks.Month, Source = ks.Event }, vs => vs.Allocation)
                .Select(g => new
                {
                    NewEventData = new EmployeeAllocatedInMonth(g.Key.EmployeeId, g.Key.Month, g.ToList()),
                    g.Key.Source,
                })
                .Select(x => new Event<EmployeeAllocatedInMonth>(x.NewEventData)
                {
                    Id = x.Source.Id,
                    Sequence = x.Source.Sequence,
                    TenantId = x.Source.TenantId,
                    Version = x.Source.Version,
                    StreamId = x.Source.StreamId,
                    StreamKey = x.Source.StreamKey,
                    Timestamp = x.Source.Timestamp
                }).ToList();

            foreach (var @event in monthlyEvents)
            {
                grouping.AddEvents($"{@event.Data.EmployeeId}|{@event.Data.Month:yyyy-MM-dd}", new[] { @event });
            }

            return Task.CompletedTask;
        }
    }

    #endregion

    public class custom_grouper_with_multiple_result_records: IntegrationContext
    {
        [Fact]
        public async Task multi_stream_projections_should_work()
        {
            var firstEmployeeId = Guid.NewGuid();
            var firstEmployeeAlocated = new EmployeeAllocated(firstEmployeeId, new List<Allocation>()
            {
                new(new DateTime(2021, 9, 3), 9),
                new(new DateTime(2021, 9, 4), 4),
                new(new DateTime(2021, 10, 3), 10),
                new(new DateTime(2021, 10, 4), 7),
            });
            theSession.Events.Append(firstEmployeeId, firstEmployeeAlocated);

            var secondEmployeeId = Guid.NewGuid();
            var secondEmployeeAlocated = new EmployeeAllocated(secondEmployeeId, new List<Allocation>()
            {
                new(new DateTime(2021, 9, 3), 1),
                new(new DateTime(2021, 9, 4), 2),
                new(new DateTime(2021, 10, 3), 3),
                new(new DateTime(2021, 10, 4), 8),
            });

            theSession.Events.Append(secondEmployeeId, secondEmployeeAlocated);

            await theSession.SaveChangesAsync();

            var firstEmployeeSeptemberId = $"{firstEmployeeId}|2021-09-01";

            var res = theSession.Query<MonthlyAllocation>().ToList();

            var firstEmployeeSeptemberAllocations =
                await theSession.LoadAsync<MonthlyAllocation>(firstEmployeeSeptemberId);
            firstEmployeeSeptemberAllocations.ShouldNotBeNull();
            firstEmployeeSeptemberAllocations.Id.ShouldBe(firstEmployeeSeptemberId);
            firstEmployeeSeptemberAllocations.EmployeeId.ShouldBe(firstEmployeeId);
            firstEmployeeSeptemberAllocations.Hours.ShouldBe(
                firstEmployeeAlocated.Allocations
                    .Where(a => a.Day.Month == 9)
                    .Sum(a => a.Hours)
            );

            var firstEmployeeOctoberId = $"{firstEmployeeId}|2021-10-01";

            var firstEmployeeOctoberAllocations = await theSession.LoadAsync<MonthlyAllocation>(firstEmployeeOctoberId);
            firstEmployeeOctoberAllocations.ShouldNotBeNull();
            firstEmployeeOctoberAllocations.Id.ShouldBe(firstEmployeeOctoberId);
            firstEmployeeOctoberAllocations.EmployeeId.ShouldBe(firstEmployeeId);
            firstEmployeeOctoberAllocations.Hours.ShouldBe(
                firstEmployeeAlocated.Allocations
                    .Where(a => a.Day.Month == 10)
                    .Sum(a => a.Hours)
            );


            var secondEmployeeSeptemberId = $"{secondEmployeeId}|2021-09-01";

            var secondEmployeeSeptemberAllocations =
                await theSession.LoadAsync<MonthlyAllocation>(secondEmployeeSeptemberId);
            secondEmployeeSeptemberAllocations.ShouldNotBeNull();
            secondEmployeeSeptemberAllocations.Id.ShouldBe(secondEmployeeSeptemberId);
            secondEmployeeSeptemberAllocations.EmployeeId.ShouldBe(secondEmployeeId);
            secondEmployeeSeptemberAllocations.Hours.ShouldBe(
                secondEmployeeAlocated.Allocations
                    .Where(a => a.Day.Month == 9)
                    .Sum(a => a.Hours)
            );

            var secondEmployeeOctoberId = $"{secondEmployeeId}|2021-10-01";

            var secondEmployeeOctoberAllocations =
                await theSession.LoadAsync<MonthlyAllocation>(secondEmployeeOctoberId);
            secondEmployeeOctoberAllocations.ShouldNotBeNull();
            secondEmployeeOctoberAllocations.Id.ShouldBe(secondEmployeeOctoberId);
            secondEmployeeOctoberAllocations.EmployeeId.ShouldBe(secondEmployeeId);
            secondEmployeeOctoberAllocations.Hours.ShouldBe(
                secondEmployeeAlocated.Allocations
                    .Where(a => a.Day.Month == 10)
                    .Sum(a => a.Hours)
            );
        }

        public custom_grouper_with_multiple_result_records(DefaultStoreFixture fixture):
            base(fixture)
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "custom_grouper_with_multiple_result_records";

                _.Projections.Add<MonthlyAllocationProjection>(ProjectionLifecycle.Inline);
            });
        }
    }
}
#endif
