using System;
using System.Collections.Generic;
using System.Linq;
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

        public ProjectionWatcher(Task completion, IList<IAsyncProjectionShard> shards)
        {
            var nameLength = shards.Select(x => x.Name.Identity.Length).Max() + 2;
            _shards.OnMissing = name => new ProjectionStatus(name, completion, nameLength);

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
        private readonly int _nameLength;

        public ProjectionStatus(string shardName, Task completion, int nameLength)
        {
            _shardName = shardName;
            _nameLength = nameLength;

            var initial = shardName.PadRight(_nameLength) + "(Waiting)".PadRight(15) + "0".PadLeft(15);


            AnsiConsole
                .Status()
                .AutoRefresh(true)
                .StartAsync(initial, context =>
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

            string name = _shardName;
            string status = "(Waiting)";
            long sequence = state.Sequence;


            switch (state.Action)
            {
                case ShardAction.Started:
                case ShardAction.Updated:
                    status = "(Running)";
                    _context.Spinner(Spinner.Known.Default);
                    _context.SpinnerStyle(Style.Plain);
                    break;

                case ShardAction.Paused:
                    status = "(Paused)";
                    _context.Spinner(Spinner.Known.Clock);
                    _context.SpinnerStyle(Style.Parse("grey italic"));
                    break;

                case ShardAction.Stopped:
                    status = "(Stopped)";
                    _context.Spinner(Spinner.Known.Clock);
                    _context.SpinnerStyle(Style.Parse("grey italic"));
                    break;
            }

            _context.Status = name.PadRight(_nameLength) + status.PadRight(15) + sequence.ToString().PadLeft(15);

            _context.Refresh();
        }
    }
}
