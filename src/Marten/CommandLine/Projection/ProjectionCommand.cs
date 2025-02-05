using System.Threading.Tasks;
using JasperFx.CommandLine;

namespace Marten.CommandLine.Projection;

[Description("Marten's asynchronous projection and projection rebuilds")]
public class ProjectionsCommand: JasperFxAsyncCommand<ProjectionInput>
{
    public override async Task<bool> Execute(ProjectionInput input)
    {
        using var host = input.BuildHost();

        var controller = new ProjectionController(new ProjectionHost(host), new ConsoleView());

        return await controller.Execute(input).ConfigureAwait(false);
    }
}
