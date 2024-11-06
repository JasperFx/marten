using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using JasperFx.Core;
using JasperFx.Events.Projections;
using OpenTelemetry.Trace;

namespace Marten.Events.Daemon.HighWater;

internal class HighWaterAgent: IDisposable
{
    private readonly IHighWaterDetector _detector;
    private readonly ILogger _logger;
    private readonly DaemonSettings _settings;
    private readonly Timer _timer;
    private readonly ShardStateTracker _tracker;

    private HighWaterStatistics _current;
    private Task<Task> _loop;
    private CancellationToken _token;
    private readonly string _spanName;
    private readonly Counter<int> _skipping;

    // ReSharper disable once ContextualLoggerProblem
    public HighWaterAgent(Meter meter, IHighWaterDetector detector, ShardStateTracker tracker,
        ILogger logger,
        DaemonSettings settings, CancellationToken token)
    {
        _detector = detector;
        _tracker = tracker;
        _logger = logger;
        _settings = settings;
        _token = token;

        _timer = new Timer(_settings.HealthCheckPollingTime.TotalMilliseconds) { AutoReset = true };
        _timer.Elapsed += TimerOnElapsed;

        _spanName = detector.DatabaseName.EqualsIgnoreCase("Marten") ? "marten.daemon.highwatermark" : $"marten.{_detector.DatabaseName.ToLowerInvariant()}.daemon.highwatermark";

        var meterName = detector.DatabaseName.EqualsIgnoreCase("Marten") ? "marten.daemon.skipping" : $"marten.{_detector.DatabaseName.ToLowerInvariant()}.daemon.skipping";
        _skipping = meter.CreateCounter<int>(meterName);
    }

    public bool IsRunning { get; private set; }

    public void Dispose()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _loop?.SafeDispose();
    }

    public async Task StartAsync()
    {
        IsRunning = true;

        _current = await _detector.Detect(_token).ConfigureAwait(false);

        _tracker.Publish(
            new ShardState(ShardState.HighWaterMark, _current.CurrentMark) { Action = ShardAction.Started });

        _loop = Task.Factory.StartNew(detectChanges, _token,
            TaskCreationOptions.LongRunning | TaskCreationOptions.AttachedToParent, TaskScheduler.Default);

        _timer.Start();

        _logger.LogInformation("Started HighWaterAgent for database {Name}", _detector.DatabaseName);
    }

    private async Task detectChanges()
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            var next = await _detector.Detect(_token).ConfigureAwait(false);
            if (_current == null || next.CurrentMark > _current.CurrentMark)
            {
                _current = next;
                _tracker.MarkHighWater(_current.CurrentMark);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed while making the initial determination of the high water mark for database {Name}", _detector.DatabaseName);
        }

        await Task.Delay(_settings.FastPollingTime, _token).ConfigureAwait(false);

        while (!_token.IsCancellationRequested)
        {
            if (!IsRunning)
            {
                break;
            }

            using var activity = MartenTracing.StartActivity(_spanName);

            HighWaterStatistics statistics = null;
            try
            {
                statistics = await _detector.Detect(_token).ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex)
            {
                if (ex.ObjectName.EqualsIgnoreCase("Npgsql.PoolingDataSource") && _token.IsCancellationRequested)
                {
                    return;
                }

                _logger.LogError(ex, "Failed while trying to detect high water statistics for database {Name}", _detector.DatabaseName);
                await Task.Delay(_settings.SlowPollingTime, _token).ConfigureAwait(false);

                activity?.RecordException(ex);

                continue;

            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed while trying to detect high water statistics for database {Name}", _detector.DatabaseName);
                activity?.RecordException(e);
                await Task.Delay(_settings.SlowPollingTime, _token).ConfigureAwait(false);
                continue;
            }

            var status = tagActivity(statistics, activity);

            switch (status)
            {
                case HighWaterStatus.Changed:
                    await markProgressAsync(statistics, _settings.FastPollingTime, status).ConfigureAwait(false);
                    break;

                case HighWaterStatus.CaughtUp:
                    await markProgressAsync(statistics, _settings.SlowPollingTime, status).ConfigureAwait(false);
                    break;

                case HighWaterStatus.Stale:
                    _logger.LogInformation("High Water agent is stale at {CurrentMark} for database {Name}", statistics.CurrentMark, _detector.DatabaseName);

                    // This gives the high water detection a chance to allow the gaps to fill in
                    // before skipping to the safe harbor time
                    var safeHarborTime = _current.Timestamp.Add(_settings.StaleSequenceThreshold);
                    if (safeHarborTime > statistics.Timestamp)
                    {
                        await Task.Delay(_settings.SlowPollingTime, _token).ConfigureAwait(false);
                        continue;
                    }

                    _logger.LogInformation(
                        "High Water agent is stale after threshold of {DelayInSeconds} seconds, skipping gap to events marked after {SafeHarborTime} for database {Name}",
                        _settings.StaleSequenceThreshold.TotalSeconds, safeHarborTime, _detector.DatabaseName);

                    activity?.SetTag("skipped", "true");

                    var lastKnown = statistics.CurrentMark;

                    statistics = await _detector.DetectInSafeZone(_token).ConfigureAwait(false);

                    status = tagActivity(statistics, activity);
                    activity?.SetTag("last.mark", lastKnown);

                    _skipping.Add(1);

                    await markProgressAsync(statistics, _settings.FastPollingTime, status).ConfigureAwait(false);
                    break;
            }
        }

        _logger.LogInformation("HighWaterAgent has detected a cancellation and has stopped polling for database {Name}", _detector.DatabaseName);
    }

    private HighWaterStatus tagActivity(HighWaterStatistics statistics, Activity activity)
    {
        var status = statistics.InterpretStatus(_current);

        activity?.AddTag("sequence", statistics.HighestSequence);
        activity?.AddTag("status", status.ToString());
        activity?.AddTag("current.mark", statistics.CurrentMark);
        return status;
    }

    private async Task markProgressAsync(HighWaterStatistics statistics, TimeSpan delayTime, HighWaterStatus status)
    {
        if (!IsRunning)
        {
            return;
        }

        // don't bother sending updates if the current position is 0
        if (statistics.CurrentMark == 0 || statistics.CurrentMark == _tracker.HighWaterMark)
        {
            if (status == HighWaterStatus.CaughtUp)
            {
                // Update the current stats if the status is not stale
                // This ensures the current stats timestamp is up-to-date, and not just set to the time of the last changed
                // Without this, the StaleSequeceThreshold gets applied to the timestamp of the last changed highwatermark
                // meaning that a time break in events larger than the StaleSequeceThreshold makes the safeHarbourTime less that the timestamp of the processing statistics
                _current = statistics;
            }

            await Task.Delay(delayTime, _token).ConfigureAwait(false);
            return;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("High Water mark detected at {CurrentMark} for database {Name}", statistics.CurrentMark, _detector.DatabaseName);
        }

        _current = statistics;

        _tracker.MarkHighWater(statistics.CurrentMark);

        await Task.Delay(delayTime, _token).ConfigureAwait(false);
    }

    private void TimerOnElapsed(object sender, ElapsedEventArgs e)
    {
        _ = checkState();
    }

    private async Task checkState()
    {
        if (_loop.IsFaulted && !_token.IsCancellationRequested)
        {
            _logger.LogError(_loop.Exception, "HighWaterAgent polling loop was faulted for database {Name}", _detector.DatabaseName);

            try
            {
                _loop.Dispose();
                await StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error trying to restart the HighWaterAgent for database {Name}", _detector.DatabaseName);
            }
        }
    }

    public async Task CheckNowAsync()
    {
        var statistics = await _detector.DetectInSafeZone(_token).ConfigureAwait(false);
        var initialHighMark = statistics.HighestSequence;

        // Get out of here if you're at the initial, empty state
        if (initialHighMark == 1 && statistics.CurrentMark == 0)
        {
            _tracker.MarkHighWater(statistics.CurrentMark);
            return;
        }

        while (statistics.CurrentMark < initialHighMark)
        {
            await Task.Delay(_settings.SlowPollingTime, _token).ConfigureAwait(false);
            statistics = await _detector.DetectInSafeZone(_token).ConfigureAwait(false);
        }

        _tracker.MarkHighWater(statistics.CurrentMark);
    }

    public async Task StopAsync()
    {
        try
        {
            _timer?.Stop();
            if (_loop != null)
            {
                await _loop.ConfigureAwait(false);
                _loop?.Dispose();
            }

            IsRunning = false;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error trying to stop the HighWaterAgent for database {Name}", _detector.DatabaseName);
        }
    }
}
