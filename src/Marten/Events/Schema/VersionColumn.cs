using System.Diagnostics.CodeAnalysis;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Npgsql;

namespace Marten.Events.Schema;

internal class VersionColumn: EventTableColumn
{
    public VersionColumn() : base("version", x => x.Version)
    {
        AllowNulls = false;
    }
}
