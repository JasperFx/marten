using JasperFx.Events;
using JasperFx.Events.Projections;

namespace Marten.Events.Aggregation;

/// <summary>
///     A batch of messages published by projection side effects within a single projection update.
/// </summary>
/// <remarks>
///     <para>
///         <b>Implementations must be thread safe.</b> The async daemon raises side effects for the event
///         slices in one batch concurrently, so <see cref="IMessageSink.PublishAsync{T}" /> is called from
///         several threads against the same <see cref="IMessageBatch" /> instance -- measured at up to 8
///         simultaneous callers across 10 threads for a single-stream projection catching up over 20
///         streams. An implementation that appends to an unsynchronized collection silently loses
///         messages under load; see #5065, where exactly that made a daemon test look flaky.
///     </para>
///     <para>
///         The same applies to any collection an <see cref="IMessageOutbox" /> uses to track the batches
///         it hands out, since <see cref="IMessageOutbox.CreateBatch" /> is reached from those threads too.
///     </para>
/// </remarks>
public interface IMessageBatch: IMessageSink, IChangeListener
{

}
