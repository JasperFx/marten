using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using Marten.Events.Daemon;

namespace Marten.Events.TestSupport;

public partial class ProjectionScenario
{
    private readonly Queue<ScenarioStep> _steps = new();
    private readonly DocumentStore _store;
    private bool _hasExecuted;

    internal ProjectionScenario(DocumentStore store)
    {
        _store = store;
    }

    internal IProjectionDaemon? Daemon { get; private set; }

    internal ScenarioStep? NextStep => _steps.Count != 0 ? _steps.Peek() : null;

    internal IDocumentSession? Session { get; private set; }

    /// <summary>
    ///     The scenario deletes all existing event data plus the storage for every
    ///     registered projection before running. Set this to false to run the
    ///     scenario on top of whatever data already exists
    /// </summary>
    public bool DeleteExistingData { get; set; } = true;

    /// <summary>
    ///     Opt into applying this scenario to a specific tenant id in the
    ///     case of using multi-tenancy of any kind
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    ///     Maximum time the scenario waits for any asynchronous projections to
    ///     catch up after each batch of appended events. Default is 30 seconds
    /// </summary>
    public TimeSpan Timeout { get; set; } = 30.Seconds();

    internal Task WaitForNonStaleData(CancellationToken ct = default)
    {
        if (Daemon == null)
        {
            return Task.CompletedTask;
        }

        return Daemon.WaitForNonStaleData(Timeout).WaitAsync(ct);
    }


    private ScenarioStep action(Action<IEventOperations> action)
    {
        var step = new ScenarioAction(action);
        _steps.Enqueue(step);

        return step;
    }

    private ScenarioStep assertion(Func<IQuerySession, CancellationToken, Task> check)
    {
        var step = new ScenarioAssertion(check);
        _steps.Enqueue(step);

        return step;
    }

    internal async Task Execute(CancellationToken ct = default)
    {
        if (_hasExecuted)
        {
            throw new InvalidOperationException(
                "This ProjectionScenario has already been executed and its steps have been consumed. Build and run a new scenario with DocumentStore.Advanced.EventProjectionScenario() instead");
        }

        _hasExecuted = true;

        if (DeleteExistingData)
        {
            await _store.Advanced.Clean.DeleteAllEventDataAsync(ct).ConfigureAwait(false);
            foreach (var storageType in
                     _store.Options.Projections.All.SelectMany(x => x.Options.StorageTypes))
                await _store.Advanced.Clean.DeleteDocumentsByTypeAsync(storageType, ct).ConfigureAwait(false);
        }

        if (_store.Options.Projections.HasAnyAsyncProjections())
        {
            Daemon = await _store.BuildProjectionDaemonAsync(TenantId).ConfigureAwait(false);
            await Daemon.StartAllAsync().ConfigureAwait(false);
        }

        Session = TenantId.IsNotEmpty() ? _store.LightweightSession(TenantId) : _store.LightweightSession();

        try
        {
            var exceptions = new List<Exception>();
            var number = 0;
            var descriptions = new List<string>();
            var actionFailed = false;

            while (_steps.Any())
            {
                number++;
                var step = _steps.Dequeue();

                try
                {
                    await step.Execute(this, ct).ConfigureAwait(false);
                    descriptions.Add($"{number.ToString().PadLeft(3)}. {step.Description}");
                }
                catch (Exception e)
                {
                    descriptions.Add($"FAILED: {number.ToString().PadLeft(3)}. {step.Description}");
                    descriptions.Add(e.ToString());
                    exceptions.Add(e);

                    // A failed action means every later step would run against a state nobody
                    // intended, so stop right here instead of piling up cascading noise. Failed
                    // assertions keep accumulating -- the state is still the intended one.
                    if (step is ScenarioAction)
                    {
                        actionFailed = true;
                        if (_steps.Count != 0)
                        {
                            descriptions.Add(
                                $"Skipped the remaining {_steps.Count} step(s) after the failed action");
                            _steps.Clear();
                        }

                        break;
                    }
                }
            }

            // A ScenarioAction only flushes when the step AFTER it is an assertion, so whatever a
            // trailing action queued is still sitting in the session -- and the finally below disposes
            // that session without committing. An append with no assertion after it is still an append,
            // and an arrange-only scenario should not be a silent no-op that passes. See #5126.
            //
            // Unconditional on purpose: SaveChangesAsync returns immediately when the unit of work is
            // empty, and WaitForNonStaleData is already a no-op when no daemon is running. Skipped when
            // an action failed -- the session may hold a partially built unit of work at that point.
            if (!actionFailed)
            {
                try
                {
                    await Session.SaveChangesAsync(ct).ConfigureAwait(false);
                    await WaitForNonStaleData(ct).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    descriptions.Add("FAILED: committing the events queued by the final step");
                    descriptions.Add(e.ToString());
                    exceptions.Add(e);
                }
            }

            if (exceptions.Any())
            {
                throw new ProjectionScenarioException(descriptions, exceptions);
            }
        }
        finally
        {
            if (Daemon != null)
            {
                await Daemon.StopAllAsync().ConfigureAwait(false);
                Daemon.SafeDispose();
            }

            Session?.SafeDispose();
        }
    }
}
