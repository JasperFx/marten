using Oakton;

namespace Marten.CommandLine.Commands
{
    [Description("Assert that the existing database matches the current Marten configuration", Name = "marten-assert")]
    public class AssertCommand: Weasel.CommandLine.AssertCommand
    {
    }
}
