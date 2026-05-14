using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Blocks;
using JasperFx.Core;
using Spectre.Console;

namespace EventPublisher;

public class StatusBoard
{
    private readonly LightweightCache<string, ProjectionStatus> _counts;
    private readonly StatusContext _context;
    private readonly Block<UpdateMessage> _updater;

    public StatusBoard(Task completion)
    {
        _counts = new(name => new ProjectionStatus(name, completion));
        _updater = new Block<UpdateMessage>(Update);
    }

    public record UpdateMessage(string Name, int Count);

    public void Update(UpdateMessage message)
    {
        _counts[message.Name].Update(message.Count);
    }

    public void Update(string name, int count)
    {
        _updater.Post(new UpdateMessage(name, count));
    }
}


public class ProjectionStatus
{
    private readonly string _name;
    private StatusContext _context;
    private int _count;

    public ProjectionStatus(string name, Task completion)
    {
        _name = name;

        AnsiConsole
            .Status()
            .AutoRefresh(true)
            .StartAsync("Waiting...", context =>
            {
                context.Spinner(Spinner.Known.Clock);
                context.SpinnerStyle(Style.Parse("grey italic"));
                context.Refresh();
                _context = context;
                return completion;
            });
    }

    public void Update(int count)
    {
        _count += count;

        if (_context == null) return;
        _context.Spinner(Spinner.Known.Default);
        _context.SpinnerStyle(Style.Plain);
        _context.Status = $"{_name}: {_count}";

        _context.Refresh();
    }

}
