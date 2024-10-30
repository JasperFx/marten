using JasperFx.CommandLine;

namespace Marten.CommandLine.Commands;

[Description("Applies all outstanding changes to the database based on the current configuration",
    Name = "marten-apply")]
public class ApplyCommand: Weasel.Core.CommandLine.ApplyCommand
{
}
