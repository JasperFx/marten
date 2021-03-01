using System;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Daemon;
using Spectre.Console;

namespace Marten.CommandLine.Commands.Projection
{
    public class ProjectionWatcher: IObserver<ShardState>
    {
        private readonly Cache<string, ProjectionStatus> _shards
            = new();

        public ProjectionWatcher(Task completion)
        {
            _shards.OnMissing = name => new ProjectionStatus(name, completion);

            _shards[ShardState.HighWaterMark].Update(new ShardState(ShardState.HighWaterMark, 0));
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(ShardState value)
        {
            _shards[value.ShardName].Update(value);
        }
    }

    public class ProjectionStatus
    {
        private StatusContext _context;
        private readonly string _shardName;

        public ProjectionStatus(string shardName, Task completion)
        {
            _shardName = shardName;

            AnsiConsole
                .Status()
                .AutoRefresh(true)
                .StartAsync($"{shardName} (Waiting)", context =>
                {
                    context.Spinner(Spinner.Known.Clock);
                    context.SpinnerStyle(Style.Parse("grey italic"));
                    context.Refresh();
                    _context = context;
                    return completion;
                });
        }

        public void Update(ShardState state)
        {
            if (state.Exception != null)
            {
                AnsiConsole.MarkupLine($"[red]Error in shard '{_shardName}'[/]");
                AnsiConsole.WriteLine(state.Exception.ToString());
            }

            switch (state.Action)
            {
                case ShardAction.Started:
                case ShardAction.Updated:
                    _context.Status = $"{_shardName} running at sequence {state.Sequence}";
                    _context.Spinner(Spinner.Known.Default);
                    _context.SpinnerStyle(Style.Plain);
                    break;

                case ShardAction.Paused:
                    _context.Status = $"{_shardName} paused at sequence {state.Sequence}";
                    _context.Spinner(Spinner.Known.Clock);
                    _context.SpinnerStyle(Style.Parse("grey italic"));
                    break;

                case ShardAction.Stopped:
                    _context.Status = $"{_shardName} stopped at sequence {state.Sequence}";
                    _context.Spinner(Spinner.Known.Clock);
                    _context.SpinnerStyle(Style.Parse("grey italic"));
                    break;
            }

            _context.Refresh();
        }
    }
}
