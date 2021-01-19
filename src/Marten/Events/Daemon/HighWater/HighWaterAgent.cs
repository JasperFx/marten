using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Marten.Events.Projections;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Marten.Events.Daemon.HighWater
{
    internal class HighWaterAgent
    {
        private readonly IHighWaterDetector _detector;
        private readonly ShardStateTracker _tracker;
        private readonly ILogger<IProjection> _logger;
        private readonly DaemonSettings _settings;
        private readonly CancellationToken _token;
        private readonly Timer _timer;
        private Task<Task> _loop;

        private HighWaterStatistics _current;

        // ReSharper disable once ContextualLoggerProblem
        public HighWaterAgent(IHighWaterDetector detector, ShardStateTracker tracker, ILogger<IProjection> logger, DaemonSettings settings, CancellationToken token)
        {
            _detector = detector;
            _tracker = tracker;
            _logger = logger;
            _settings = settings;
            _token = token;

            //_timer = new Timer(_settings.PollingTime) {AutoReset = false};
            //_timer.Elapsed += TimerOnElapsed;
        }

        public void Start()
        {
            // TODO -- make sure there's a timer trying to restart it????
            _loop = Task.Factory.StartNew(DetectChanges, TaskCreationOptions.LongRunning);


        }

        private async Task DetectChanges()
        {
            _current = await _detector.Detect(_token);

            _tracker.MarkHighWater(_current.CurrentMark);

            await Task.Delay(_settings.PollingTime, _token);

            while (!_token.IsCancellationRequested)
            {
                var statistics = await _detector.Detect(_token);
                // This is where it gets harder.
                // if changed, pass on the next change
                // if changed by a lot, speed up the polling
                // back pressure?????
                // if not changing, go to notify???
                // if high water hasn't changed, but sequence is higher, go to safe zone
            }
        }

        private void TimerOnElapsed(object sender, ElapsedEventArgs e)
        {

        }
    }
}
