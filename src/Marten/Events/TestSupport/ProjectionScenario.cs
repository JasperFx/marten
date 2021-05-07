using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Baseline.Dates;
using Marten.Events.Daemon;

namespace Marten.Events.TestSupport
{
    public partial class ProjectionScenario: IEventOperations
    {
        private readonly Queue<ScenarioStep> _steps = new();
        private readonly DocumentStore _store;


        public ProjectionScenario(DocumentStore store)
        {
            _store = store;
        }

        internal IProjectionDaemon Daemon { get; private set; }

        internal ScenarioStep NextStep => _steps.Any() ? _steps.Peek() : null;

        internal IDocumentSession Session { get; private set; }

        /// <summary>
        ///     Disable the scenario from "cleaning" out any existing
        ///     event and projected document data before running the scenario
        /// </summary>
        public bool DoNotDeleteExistingData { get; set; }



        internal Task WaitForNonStaleData()
        {
            if (Daemon == null)
            {
                return Task.CompletedTask;
            }

            return Daemon.WaitForNonStaleData(30.Seconds());
        }


        private ScenarioStep action(Action<IEventOperations> action)
        {
            var step = new ScenarioAction(action);
            _steps.Enqueue(step);

            return step;
        }

        private ScenarioStep assertion(Func<IQuerySession, Task> check)
        {
            var step = new ScenarioAssertion(check);
            _steps.Enqueue(step);

            return step;
        }

        internal async Task Execute()
        {
            if (!DoNotDeleteExistingData)
            {
                _store.Advanced.Clean.DeleteAllEventData();
                foreach (var storageType in
                    _store.Events.Projections.Projections.SelectMany(x => x.Options.StorageTypes))
                    await _store.Advanced.Clean.DeleteDocumentsByTypeAsync(storageType);
            }

            if (_store.Events.Projections.HasAnyAsyncProjections())
            {
                Daemon = _store.BuildProjectionDaemon();
                await Daemon.StartAllShards();
            }

            Session = _store.LightweightSession();

            try
            {
                var exceptions = new List<Exception>();
                var number = 0;
                var descriptions = new List<string>();

                while (_steps.Any())
                {
                    number++;
                    var step = _steps.Dequeue();

                    try
                    {
                        await step.Execute(this);
                        descriptions.Add($"{number.ToString().PadLeft(3)}. {step.Description}");
                    }
                    catch (Exception e)
                    {
                        descriptions.Add($"FAILED: {number.ToString().PadLeft(3)}. {step.Description}");
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
                    await Daemon.StopAll();
                    Daemon.SafeDispose();
                }

                Session?.SafeDispose();
            }
        }
    }
}
