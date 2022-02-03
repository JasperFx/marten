#if NET
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

#nullable enable

namespace EventSourcingTests.Aggregation
{
    #region sample_aggregate-stream-into-state-definition

    public record AccountingMonthOpened(
        Guid FinancialAccountId,
        int Month,
        int Year,
        decimal StartingBalance
    );

    public record InflowRecorded(
        Guid FinancialAccountId,
        decimal TransactionAmount
    );

    public record CashWithdrawnFromATM(
        Guid FinancialAccountId,
        decimal CashAmount
    );

    public record AccountingMonthClosed(
        Guid FinancialAccountId,
        int Month,
        int Year,
        decimal FinalBalance
    );

    public class FinancialAccount
    {
        public Guid Id { get; private set; }
        public int CurrentMonth { get; private set; }
        public int CurrentYear { get; private set; }
        public bool IsOpened { get; private set; }
        public decimal Balance { get; private set; }
        public int Version { get; private set; }

        public void Apply(AccountingMonthOpened @event)
        {
            Id = @event.FinancialAccountId;
            CurrentMonth = @event.Month;
            CurrentYear = @event.Year;
            Balance = @event.StartingBalance;
            IsOpened = true;
            Version++;
        }

        public void Apply(InflowRecorded @event)
        {
            Balance += @event.TransactionAmount;

            Version++;
        }

        public void Apply(CashWithdrawnFromATM @event)
        {
            Balance -= @event.CashAmount;
            Version++;
        }

        public void Apply(AccountingMonthClosed @event)
        {
            IsOpened = false;
            Version++;
        }
    }

    #endregion


    #region sample_aggregate-stream-into-state-wrapper

    public class CashRegisterRepository
    {
        private IDocumentSession session;

        public CashRegisterRepository(IDocumentSession session)
        {
            this.session = session;
        }

        public Task Store(
            FinancialAccount financialAccount,
            object @event,
            CancellationToken ct = default
        )
        {
            if (@event is AccountingMonthOpened)
            {
                session.Store(financialAccount);
            }

            session.Events.Append(financialAccount.Id, @event);

            return session.SaveChangesAsync(ct);
        }

        public async Task<FinancialAccount?> Get(
            Guid cashRegisterId,
            CancellationToken ct = default
        )
        {
            var cashRegister =
                await session.LoadAsync<FinancialAccount>(cashRegisterId, ct);

            var fromVersion = cashRegister != null
                ?
                // incrementing version to not apply the same event twice
                cashRegister.Version + 1
                : 0;

            return await session.Events.AggregateStreamAsync(
                cashRegisterId,
                state: cashRegister,
                fromVersion: fromVersion,
                token: ct
            );
        }
    }

    #endregion

    public class aggregate_stream_into_samples: OneOffConfigurationsContext
    {
        public async Task SampleAggregateStreamIntoState()
        {
            var streamId = Guid.NewGuid();
            var baseState = new object();
            const int baseStateVersion = 1;

            #region sample_aggregate-stream-into-state-default
            await theSession.Events.AggregateStreamAsync(
                streamId,
                state: baseState,
                fromVersion: baseStateVersion
            );
            #endregion
        }

        [Fact]
        public async Task AggregatingStreamIntoShouldUseState()
        {
            var financialAccountId = Guid.NewGuid();

            await AppendEvent(
                financialAccountId,
                new AccountingMonthOpened(financialAccountId, 10, 2021, 0),
                new InflowRecorded(financialAccountId, 100),
                new InflowRecorded(financialAccountId, 100),
                new InflowRecorded(financialAccountId, 100),
                new AccountingMonthClosed(financialAccountId, 10, 2021, 300)
            );

            #region sample_aggregate-stream-into-state-store

            (FinancialAccount, AccountingMonthOpened) OpenAccountingMonth(
                FinancialAccount cashRegister)
            {
                var @event = new AccountingMonthOpened(
                    cashRegister.Id, 11, 2021, 300);

                cashRegister.Apply(@event);
                return (cashRegister, @event);
            }

            var closedCashierShift =
                await theSession.Events.AggregateStreamAsync<FinancialAccount>(
                    financialAccountId
                );

            var (openedCashierShift, cashierShiftOpened) =
                OpenAccountingMonth(closedCashierShift!);

            var repository = new CashRegisterRepository(theSession);

            await repository.Store(openedCashierShift, cashierShiftOpened);

            #endregion

            var snapshot = await theSession.LoadAsync<FinancialAccount>(financialAccountId);

            snapshot.ShouldNotBeNull();
            snapshot.Id.ShouldBe(financialAccountId);
            snapshot.CurrentMonth.ShouldBe(11);
            snapshot.CurrentYear.ShouldBe(2021);
            snapshot.Balance.ShouldBe(300);
            snapshot.IsOpened.ShouldBeTrue();
            snapshot.Version.ShouldBe(openedCashierShift.Version);

            await AppendEvent(
                financialAccountId,
                new InflowRecorded(financialAccountId, 100),
                new InflowRecorded(financialAccountId, 100),
                new InflowRecorded(financialAccountId, 100)
            );

            #region sample_aggregate-stream-into-state-get

            var currentState = await repository.Get(financialAccountId);

            #endregion

            currentState.ShouldNotBeNull();
            currentState.Id.ShouldBe(financialAccountId);
            snapshot.CurrentMonth.ShouldBe(11);
            snapshot.CurrentYear.ShouldBe(2021);
            currentState.Balance.ShouldBe(600);
            currentState.IsOpened.ShouldBeTrue();
            currentState.Version.ShouldBe(openedCashierShift.Version + 3);
        }

        public aggregate_stream_into_samples()
        {
            StoreOptions(options =>
                options.UseDefaultSerialization(nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters)
            );
        }
    }
}
#endif
