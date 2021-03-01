using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Marten.Events.Projections;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Marten.Events.Daemon.HighWater
{
    internal class HighWaterAgent : IDisposable
    {
        private readonly IHighWaterDetector _detector;
        private readonly ShardStateTracker _tracker;
        private readonly ILogger _logger;
        private readonly DaemonSettings _settings;
        private readonly CancellationToken _token;
        private readonly Timer _timer;
        private Task<Task> _loop;

        private HighWaterStatistics _current;

        // ReSharper disable once ContextualLoggerProblem
        public HighWaterAgent(IHighWaterDetector detector, ShardStateTracker tracker, ILogger logger, DaemonSettings settings, CancellationToken token)
        {
            _detector = detector;
            _tracker = tracker;
            _logger = logger;
            _settings = settings;
            _token = token;

            _timer = new Timer(_settings.HealthCheckPollingTime.TotalMilliseconds) {AutoReset = true};
            _timer.Elapsed += TimerOnElapsed;
        }

        public async Task Start()
        {
            _current = await _detector.Detect(_token);

            _tracker.Publish(new ShardState(ShardState.HighWaterMark, _current.CurrentMark){Action = ShardAction.Started});

            _loop = Task.Factory.StartNew(DetectChanges, TaskCreationOptions.LongRunning | TaskCreationOptions.AttachedToParent);

            _timer.Start();

            _logger.LogInformation("Started HighWaterAgent");
        }


        private async Task DetectChanges()
        {
            // TODO -- need to put some retry & exception handling here.
            try
            {
                _current = await _detector.Detect(_token);

                if (_current.CurrentMark > 0)
                {
                    _tracker.MarkHighWater(_current.CurrentMark);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed while making the initial determination of the high water mark");
            }

            await Task.Delay(_settings.FastPollingTime, _token);

            while (!_token.IsCancellationRequested)
            {
                HighWaterStatistics statistics = null;
                try
                {
                    statistics = await _detector.Detect(_token);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed while trying to detect high water statistics");
                    await Task.Delay(_settings.SlowPollingTime, _token);
                    continue;
                }

                var status = statistics.InterpretStatus(_current);

                switch (status)
                {
                    case HighWaterStatus.Changed:
                        await markProgress(statistics, _settings.FastPollingTime);
                        break;

                    case HighWaterStatus.CaughtUp:
                        await markProgress(statistics, _settings.SlowPollingTime);
                        break;

                    case HighWaterStatus.Stale:
                        var safeHarborTime = _current.Timestamp.Add(_settings.StaleSequenceThreshold);
                        var delayTime = safeHarborTime.Subtract(statistics.Timestamp);
                        if (delayTime.TotalSeconds > 0)
                        {
                            await Task.Delay(delayTime, _token);
                        }

                        statistics = await _detector.DetectInSafeZone(safeHarborTime, _token);
                        await markProgress(statistics, _settings.FastPollingTime);
                        break;
                }
            }

            _logger.LogInformation("HighWaterAgent has detected a cancellation and has stopped polling");
        }

        private async Task markProgress(HighWaterStatistics statistics, TimeSpan delayTime)
        {
            // don't bother sending updates if the current position is 0
            if (statistics.CurrentMark == 0 || statistics.CurrentMark == _tracker.HighWaterMark) return;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("High Water mark detected at {CurrentMark}", statistics.CurrentMark);
            }
            _current = statistics;
            _tracker.MarkHighWater(statistics.CurrentMark);
            await Task.Delay(delayTime, _token);
        }

        private void TimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            if (_loop.IsFaulted && !_token.IsCancellationRequested)
            {
                _logger.LogError(_loop.Exception,"HighWaterAgent polling loop was faulted");

                try
                {
                    _loop.Dispose();
                    Start().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error trying to restart the HighWaterAgent");
                }
            }


        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _loop?.Dispose();
        }

        public async Task CheckNow()
        {
            var statistics = await _detector.Detect(_token);
            _tracker.MarkHighWater(statistics.CurrentMark);
        }
    }
}
