namespace Marten;

/// <summary>
/// Allows projections to register <see cref="ITransactionParticipant"/> instances
/// that will participate in the same database transaction as Marten's batch operations.
/// </summary>
public interface ITransactionParticipantRegistrar
{
    void AddTransactionParticipant(ITransactionParticipant participant);
}
