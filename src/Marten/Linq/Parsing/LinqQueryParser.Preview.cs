#nullable enable
using Weasel.Postgresql;

namespace Marten.Linq.Parsing;

internal partial class LinqQueryParser
{
    public void BuildDiagnosticCommand(FetchType fetchType, CommandBuilder sql)
    {
        var statements = BuildStatements();

        switch (fetchType)
        {
            case FetchType.Any:
                statements.MainSelector.ToAny();
                break;

            case FetchType.Count:
                statements.MainSelector.ToCount<long>();
                break;

            case FetchType.FetchOne:
                statements.MainSelector.Limit = 1;
                break;
        }

        statements.Top.Apply(sql);
    }
}
