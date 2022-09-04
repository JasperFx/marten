using System.Threading.Tasks;
using Oakton;

namespace Marten.CommandLine.Commands.Projection;

[Description("Marten's asynchronous projection and projection rebuilds")]
public class ProjectionsCommand: OaktonAsyncCommand<ProjectionInput>
{
    public override Task<bool> Execute(ProjectionInput input)
    {
        using var host = input.BuildHost();

        var controller = new ProjectionController(new ProjectionHost(host), new ConsoleView());

        return controller.Execute(input);
    }
}