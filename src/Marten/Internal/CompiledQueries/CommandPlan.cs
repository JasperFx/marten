using System.Collections.Generic;

namespace Marten.Internal.CompiledQueries;

internal class CommandPlan
{
    public string CommandText { get; set; } = string.Empty;
    public List<ParameterUsage> Parameters { get; } = new();
}