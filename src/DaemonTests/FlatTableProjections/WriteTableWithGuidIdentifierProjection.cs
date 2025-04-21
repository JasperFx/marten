using System;
using Marten.Events.Projections.Flattened;

namespace DaemonTests.FlatTableProjections;

public class WriteTableWithGuidIdentifierProjection: FlatTableProjection
{
    public WriteTableWithGuidIdentifierProjection(): base("values", SchemaNameSource.DocumentSchema)
    {
        Name = "Values";

        // Gotta have a primary key
        Table.AddColumn<Guid>("id").AsPrimaryKey();

        Project<ValuesSet>(cmd =>
        {
            // Derive column name by default, add column if does not exist
            cmd.Map(x => x.A); // returns the column
            cmd.Map(x => x.B);
            cmd.Map(x => x.C);
            cmd.Map(x => x.D);

            cmd.SetValue("status", "new");
            cmd.SetValue("revision", 1);
        });

        Project<ValuesAdded>(cmd =>
        {
            // Derive column name by default, add column if does not exist
            cmd.Increment(x => x.A); // returns the column
            cmd.Increment(x => x.B);
            cmd.Increment(x => x.C);
            cmd.Increment(x => x.D);

            cmd.Increment("revision"); // assume to be an int or long here

            cmd.SetValue("status", "old");
        });

        Project<ValuesSubtracted>(cmd =>
        {
            // Derive column name by default, add column if does not exist
            cmd.Decrement(x => x.A); // returns the column
            cmd.Decrement(x => x.B);
            cmd.Decrement(x => x.C);
            cmd.Decrement(x => x.D);

            cmd.Decrement("revision"); // assume to be an int or long here

            cmd.SetValue("status", "old");
        });

        Delete<ValuesDeleted>();
    }
}
