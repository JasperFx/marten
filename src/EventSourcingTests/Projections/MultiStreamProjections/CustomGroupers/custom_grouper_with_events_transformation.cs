using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.ViewProjections.CustomGroupers
{
    public record Allocation(
        DateOnly Day,
        double Hours
    );

    public record EmployeeAllocated(
        Guid EmployeeId,
        List<Allocation> Allocations
    );

    public record EmployeeAllocatedInMonth(
        Guid EmployeeId,
        DateOnly Month,
        List<Allocation> Allocations
    );

    public class MonthlyAllocation
    {
        public string Id { get; set; }
        public Guid EmployeeId { get; set; }
        public DateOnly Month { get; set; }
        public double Hours { get; set; }
    }


    #region sample_view-custom-grouper-with-transformation-projection

    public class MonthlyAllocationProjection: MultiStreamProjection<MonthlyAllocation, string>
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

    #endregion sample_view-custom-grouper-with-transformation-projection

    #region sample_view-custom-grouper-with-transformation-grouper

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
                        #region sample_view-custom-grouper-with-transformation-grouper-with-data

                        Key = $"{monthlyAllocation.Key.EmployeeId}|{monthlyAllocation.Key.Month:yyyy-MM-dd}",
                        Event = monthlyAllocation.Key.Source.WithData(
                            new EmployeeAllocatedInMonth(
                                monthlyAllocation.Key.EmployeeId,
                                monthlyAllocation.Key.Month,
                                monthlyAllocation.Select(a => a.Allocation).ToList())
                        )

                        #endregion sample_view-custom-grouper-with-transformation-grouper-with-data
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

    #endregion sample_view-custom-grouper-with-transformation-grouper

    public class custom_grouper_with_events_transformation: OneOffConfigurationsContext
    {
        [Fact]
        public async Task multi_stream_projections_should_work()
        {
            var firstEmployeeId = Guid.NewGuid();
            var firstEmployeeAllocated = new EmployeeAllocated(firstEmployeeId, new List<Allocation>()
            {
                new(new DateOnly(2021, 9, 3), 9),
                new(new DateOnly(2021, 9, 4), 4),
                new(new DateOnly(2021, 10, 3), 10),
                new(new DateOnly(2021, 10, 4), 7),
            });
            theSession.Events.Append(firstEmployeeId, firstEmployeeAllocated);

            var secondEmployeeId = Guid.NewGuid();
            var secondEmployeeAllocated = new EmployeeAllocated(secondEmployeeId, new List<Allocation>()
            {
                new(new DateOnly(2021, 9, 3), 1),
                new(new DateOnly(2021, 9, 4), 2),
                new(new DateOnly(2021, 10, 3), 3),
                new(new DateOnly(2021, 10, 4), 8),
            });

            theSession.Events.Append(secondEmployeeId, secondEmployeeAllocated);

            await theSession.SaveChangesAsync();

            var firstEmployeeSeptemberId = $"{firstEmployeeId}|2021-09-01";

            var firstEmployeeSeptemberAllocations =
                await theSession.LoadAsync<MonthlyAllocation>(firstEmployeeSeptemberId);
            firstEmployeeSeptemberAllocations.ShouldNotBeNull();
            firstEmployeeSeptemberAllocations.Id.ShouldBe(firstEmployeeSeptemberId);
            firstEmployeeSeptemberAllocations.EmployeeId.ShouldBe(firstEmployeeId);
            firstEmployeeSeptemberAllocations.Hours.ShouldBe(
                firstEmployeeAllocated.Allocations
                    .Where(a => a.Day.Month == 9)
                    .Sum(a => a.Hours)
            );

            var firstEmployeeOctoberId = $"{firstEmployeeId}|2021-10-01";

            var firstEmployeeOctoberAllocations = await theSession.LoadAsync<MonthlyAllocation>(firstEmployeeOctoberId);
            firstEmployeeOctoberAllocations.ShouldNotBeNull();
            firstEmployeeOctoberAllocations.Id.ShouldBe(firstEmployeeOctoberId);
            firstEmployeeOctoberAllocations.EmployeeId.ShouldBe(firstEmployeeId);
            firstEmployeeOctoberAllocations.Hours.ShouldBe(
                firstEmployeeAllocated.Allocations
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
                secondEmployeeAllocated.Allocations
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
                secondEmployeeAllocated.Allocations
                    .Where(a => a.Day.Month == 10)
                    .Sum(a => a.Hours)
            );
        }

        public custom_grouper_with_events_transformation()
        {
            StoreOptions(_ =>
            {
                _.Projections.Add<MonthlyAllocationProjection>(ProjectionLifecycle.Inline);
            });
        }
    }

    public static class DateTimeExtensionMethods
    {
        public static DateOnly ToStartOfMonth(this DateOnly date)
        {
            return new DateOnly(date.Year, date.Month, 1);
        }
    }
}
