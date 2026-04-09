using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.MultiStreamProjections;

#region sample_monthly_account_activity_events

public record AccountOpened(string AccountName);
public record DepositRecorded(decimal Amount);
public record WithdrawalRecorded(decimal Amount);
public record FeeCharged(decimal Amount, string Reason);

#endregion

#region sample_monthly_account_activity_document

/// <summary>
/// Read model that summarizes account activity for a single calendar month.
/// The Id is a composite key: "{streamId}:{yyyy-MM}"
/// </summary>
public class MonthlyAccountActivity
{
    public string Id { get; set; } = "";
    public Guid AccountId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int TransactionCount { get; set; }
    public decimal TotalDeposits { get; set; }
    public decimal TotalWithdrawals { get; set; }
    public decimal TotalFees { get; set; }
}

#endregion

#region sample_monthly_account_activity_projection

public class MonthlyAccountActivityProjection : MultiStreamProjection<MonthlyAccountActivity, string>
{
    public MonthlyAccountActivityProjection()
    {
        // Route each event to a document keyed by "{accountId}:{yyyy-MM}"
        // using the stream ID (account) + event timestamp (month)
        Identity<IEvent<DepositRecorded>>(e =>
            $"{e.StreamId}:{e.Timestamp:yyyy-MM}");

        Identity<IEvent<WithdrawalRecorded>>(e =>
            $"{e.StreamId}:{e.Timestamp:yyyy-MM}");

        Identity<IEvent<FeeCharged>>(e =>
            $"{e.StreamId}:{e.Timestamp:yyyy-MM}");
    }

    public MonthlyAccountActivity Create(IEvent<DepositRecorded> e)
    {
        var (accountId, year, month) = ParseKey(e);
        return new MonthlyAccountActivity
        {
            AccountId = accountId, Year = year, Month = month,
            TransactionCount = 1, TotalDeposits = e.Data.Amount
        };
    }

    public void Apply(IEvent<DepositRecorded> e, MonthlyAccountActivity activity)
    {
        activity.TransactionCount++;
        activity.TotalDeposits += e.Data.Amount;
    }

    public MonthlyAccountActivity Create(IEvent<WithdrawalRecorded> e)
    {
        var (accountId, year, month) = ParseKey(e);
        return new MonthlyAccountActivity
        {
            AccountId = accountId, Year = year, Month = month,
            TransactionCount = 1, TotalWithdrawals = e.Data.Amount
        };
    }

    public void Apply(IEvent<WithdrawalRecorded> e, MonthlyAccountActivity activity)
    {
        activity.TransactionCount++;
        activity.TotalWithdrawals += e.Data.Amount;
    }

    public MonthlyAccountActivity Create(IEvent<FeeCharged> e)
    {
        var (accountId, year, month) = ParseKey(e);
        return new MonthlyAccountActivity
        {
            AccountId = accountId, Year = year, Month = month,
            TransactionCount = 1, TotalFees = e.Data.Amount
        };
    }

    public void Apply(IEvent<FeeCharged> e, MonthlyAccountActivity activity)
    {
        activity.TransactionCount++;
        activity.TotalFees += e.Data.Amount;
    }

    private static (Guid AccountId, int Year, int Month) ParseKey(IEvent e)
    {
        return (e.StreamId, e.Timestamp.Year, e.Timestamp.Month);
    }
}

#endregion

public class monthly_account_activity_projection_tests : OneOffConfigurationsContext
{
    [Fact]
    public async Task events_across_months_produce_separate_documents()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<MonthlyAccountActivityProjection>(ProjectionLifecycle.Inline);
        });

        var accountId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            // January transactions
            var jan = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
            session.Events.StartStream(accountId,
                new AccountOpened("Checking")
                    .AsEvent().AtTimestamp(jan),
                new DepositRecorded(1000m)
                    .AsEvent().AtTimestamp(jan),
                new WithdrawalRecorded(200m)
                    .AsEvent().AtTimestamp(jan.AddDays(5)),
                new FeeCharged(5m, "Monthly fee")
                    .AsEvent().AtTimestamp(jan.AddDays(10))
            );
            await session.SaveChangesAsync();

            // February transactions
            var feb = new DateTimeOffset(2026, 2, 3, 10, 0, 0, TimeSpan.Zero);
            session.Events.Append(accountId,
                new DepositRecorded(2000m)
                    .AsEvent().AtTimestamp(feb),
                new DepositRecorded(500m)
                    .AsEvent().AtTimestamp(feb.AddDays(10))
            );
            await session.SaveChangesAsync();
        }

        // Verify January document
        await using (var query = theStore.QuerySession())
        {
            var janKey = $"{accountId}:2026-01";
            var janActivity = await query.LoadAsync<MonthlyAccountActivity>(janKey);
            janActivity.ShouldNotBeNull();
            janActivity.AccountId.ShouldBe(accountId);
            janActivity.Year.ShouldBe(2026);
            janActivity.Month.ShouldBe(1);
            janActivity.TransactionCount.ShouldBe(3); // deposit + withdrawal + fee
            janActivity.TotalDeposits.ShouldBe(1000m);
            janActivity.TotalWithdrawals.ShouldBe(200m);
            janActivity.TotalFees.ShouldBe(5m);

            // Verify February document
            var febKey = $"{accountId}:2026-02";
            var febActivity = await query.LoadAsync<MonthlyAccountActivity>(febKey);
            febActivity.ShouldNotBeNull();
            febActivity.Year.ShouldBe(2026);
            febActivity.Month.ShouldBe(2);
            febActivity.TransactionCount.ShouldBe(2); // two deposits
            febActivity.TotalDeposits.ShouldBe(2500m);
            febActivity.TotalWithdrawals.ShouldBe(0m);
        }
    }

    [Fact]
    public async Task multiple_accounts_same_month_are_separate()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<MonthlyAccountActivityProjection>(ProjectionLifecycle.Inline);
        });

        var account1 = Guid.NewGuid();
        var account2 = Guid.NewGuid();
        var jan = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero);

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(account1,
                new AccountOpened("Checking"),
                new DepositRecorded(500m).AsEvent().AtTimestamp(jan)
            );
            session.Events.StartStream(account2,
                new AccountOpened("Savings"),
                new DepositRecorded(1000m).AsEvent().AtTimestamp(jan)
            );
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();

        var a1 = await query.LoadAsync<MonthlyAccountActivity>($"{account1}:2026-01");
        var a2 = await query.LoadAsync<MonthlyAccountActivity>($"{account2}:2026-01");

        a1.ShouldNotBeNull();
        a1.TotalDeposits.ShouldBe(500m);

        a2.ShouldNotBeNull();
        a2.TotalDeposits.ShouldBe(1000m);
    }
}
