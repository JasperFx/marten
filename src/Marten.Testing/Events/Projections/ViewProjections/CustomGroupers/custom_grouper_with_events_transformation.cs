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
    #region sample_view-custom-grouper-with-multiple-result-records

    public record Allocation(
        DateTime Day,
        double Hours
    );

    public record EmployeeAllocated(
        Guid EmployeeId,
        List<Allocation> Allocations
    );

    public record EmployeeAllocatedInMonth(
        Guid EmployeeId,
        DateTime Month,
        List<Allocation> Allocations
    );

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
            TransformsEvent<EmployeeAllocated>();
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
        public Task Group(
            IQuerySession session,
            IEnumerable<IEvent> events,
            ITenantSliceGroup<string> grouping
        )
        {
            var allocations = events
                .OfType<IEvent<EmployeeAllocated>>();

            var monthlyAllocations = allocations
                .SelectMany(@event =>
                    @event.Data.Allocations.Select(
                        allocation => new
                        {
                            @event.Data.EmployeeId,
                            Allocation = allocation,
                            Month = allocation.Day.ToStartOfMonth(),
                            Source = @event
                        }
                    )
                )
                .GroupBy(allocation =>
                    new { allocation.EmployeeId, allocation.Month, allocation.Source }
                )
                .Select(monthlyAllocation =>
                    new
                    {
                        Key = $"{monthlyAllocation.Key.EmployeeId}|{monthlyAllocation.Key.Month:yyyy-MM-dd}",
                        Event = monthlyAllocation.Key.Source.WithData(
                            new EmployeeAllocatedInMonth(
                                monthlyAllocation.Key.EmployeeId,
                                monthlyAllocation.Key.Month,
                                monthlyAllocation.Select(a => a.Allocation).ToList())
                        )
                    }
                );

            foreach (var monthlyAllocation in monthlyAllocations)
            {
                grouping.AddEvents(
                    monthlyAllocation.Key,
                    new[] { monthlyAllocation.Event }
                );
            }

            return Task.CompletedTask;
        }
    }

    #endregion

    public class custom_grouper_with_events_transformation: IntegrationContext
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

        public custom_grouper_with_events_transformation(DefaultStoreFixture fixture):
            base(fixture)
        {
            StoreOptions(_ =>
            {
                _.DatabaseSchemaName = "custom_grouper_with_multiple_result_records";

                _.Projections.Add<MonthlyAllocationProjection>(ProjectionLifecycle.Inline);
            });
        }
    }

    public static class DateTimeExtensionMethods
    {
        public static DateTime ToStartOfMonth(this DateTime date)
        {
            return new DateTime(date.Year, date.Month, 1);
        }
    }
}
#endif
