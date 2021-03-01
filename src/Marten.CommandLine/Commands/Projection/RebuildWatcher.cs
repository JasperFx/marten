using System;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Spectre.Console;

namespace Marten.CommandLine.Commands.Projection
{
    public class RebuildWatcher : IObserver<ShardState>
    {
        private readonly Cache<string, ProgressTask> _shards
            = new();

        private ProgressContext _context;

        public RebuildWatcher(long highWaterMark, Task completion)
        {
            AnsiConsole.Progress()
                .AutoRefresh(false)
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(), // Task description
                    new ProgressBarColumn(), // Progress bar
                    new PercentageColumn(), // Percentage
                    new SpinnerColumn() // Spinner
                })
                .StartAsync(c =>
                {
                    _context = c;

                    return completion;
                });

            _shards.OnMissing = shardName =>
            {
                return _context.AddTask(shardName, new ProgressTaskSettings
                {
                    AutoStart = true,
                    MaxValue = highWaterMark
                });
            };
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(ShardState value)
        {
            var task = _shards[value.ShardName];
            var increment = value.Sequence - task.Value;
            task.Increment(increment);

            _context.Refresh();
        }

    }
}
