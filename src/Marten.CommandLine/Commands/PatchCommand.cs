using Oakton;

namespace Marten.CommandLine.Commands;

[Description(
    "Evaluates the current configuration against the database and writes a patch and drop file if there are any differences", Name = "marten-patch"
)]
public class PatchCommand: Weasel.CommandLine.PatchCommand
{
    public PatchCommand()
    {
        Usage("Write the patch and matching drop file").Arguments(x => x.FileName);
    }
}