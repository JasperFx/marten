using System;
using Marten.Events.Projections.Flattened;

namespace JasperFx.Events.ComplianceTests;

/*
 * Marten's half of the shared flat-table compliance projection.
 *
 * The compliance library owns the table name, the projection name and every event mapping; this
 * partial supplies the two things that cannot be portable. Flat-table projection bases take
 * constructor arguments describing where the table lives and those signatures genuinely differ
 * between products, and the column-declaration API hangs off each dialect's own Weasel Table type.
 *
 * SchemaNameSource.DocumentSchema rather than a literal, so the table follows whatever schema
 * MartenComplianceFixture built the store in.
 *
 * It lives beside the fixture rather than in EventSourcingTests because both assemblies reference
 * the source-only compliance package, so both compile the suite's half of this partial and both
 * need this half to satisfy it. EventSourcingTests links the file in, same as the fixture itself.
 */
public partial class ComplianceFlatTableProjection: FlatTableProjection
{
    public ComplianceFlatTableProjection(): base(TableName, SchemaNameSource.DocumentSchema)
    {
        Table.AddColumn<Guid>("id").AsPrimaryKey();

        ConfigureMappings();
    }
}
