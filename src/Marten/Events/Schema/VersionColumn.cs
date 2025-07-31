namespace Marten.Events.Schema;

internal class VersionColumn: EventTableColumn
{
    public VersionColumn() : base("version", x => x.Version)
    {
        AllowNulls = false;
    }
}
