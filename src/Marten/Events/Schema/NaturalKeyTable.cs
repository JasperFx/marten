using System.IO;
using System.Linq;
using JasperFx.Events;
using Marten.Events.Archiving;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class NaturalKeyTable: Table, ISchemaObject
{
    public NaturalKeyTable(EventGraph events, NaturalKeyDefinition naturalKey)
        : base(new PostgresqlObjectName(events.DatabaseSchemaName,
            $"mt_natural_key_{naturalKey.AggregateType.Name.ToLowerInvariant()}"))
    {
        // Determine the column type for the natural key value
        var columnType = naturalKey.InnerType == typeof(int) ? "integer"
            : naturalKey.InnerType == typeof(long) ? "bigint"
            : "varchar(200)";

        AddColumn("natural_key_value", columnType).AsPrimaryKey().NotNull();

        // Stream identity column - matches mt_streams.id type
        var streamCol = events.StreamIdentity == StreamIdentity.AsGuid ? "stream_id" : "stream_key";
        var streamColType = events.StreamIdentity == StreamIdentity.AsGuid ? "uuid" : "varchar(250)";

        AddColumn(streamCol, streamColType).NotNull();

        // Tenancy support - tenant_id is part of PK so same natural key can exist in different tenants
        var isConjoined = events.TenancyStyle == TenancyStyle.Conjoined;
        if (isConjoined)
        {
            AddColumn<TenantIdColumn>().AsPrimaryKey();
        }

        // Archive support
        var archiving = AddColumn<IsArchivedColumn>();
        if (events.UseArchivedStreamPartitioning)
        {
            archiving.PartitionByListValues().AddPartition("archived", true);

            if (isConjoined)
            {
                // FK must include tenant_id and is_archived to match mt_streams composite PK
                ForeignKeys.Add(new ForeignKey(PostgresqlIdentifier.Shorten($"fk_{Identifier.Name}_stream_tenant_is_archived"))
                {
                    ColumnNames = new[] { streamCol, TenantIdColumn.Name, "is_archived" },
                    LinkedNames = new[] { "id", TenantIdColumn.Name, "is_archived" },
                    LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, StreamsTable.TableName),
                    OnDelete = Weasel.Postgresql.CascadeAction.Cascade
                });
            }
            else
            {
                // FK to mt_streams must include is_archived when streams table is partitioned
                ForeignKeys.Add(new ForeignKey(PostgresqlIdentifier.Shorten($"fk_{Identifier.Name}_stream_is_archived"))
                {
                    ColumnNames = new[] { streamCol, "is_archived" },
                    LinkedNames = new[] { "id", "is_archived" },
                    LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, StreamsTable.TableName),
                    OnDelete = Weasel.Postgresql.CascadeAction.Cascade
                });
            }
        }
        else if (isConjoined)
        {
            // FK must include tenant_id to match mt_streams composite PK (tenant_id, id)
            ForeignKeys.Add(new ForeignKey(PostgresqlIdentifier.Shorten($"fk_{Identifier.Name}_stream_tenant"))
            {
                ColumnNames = new[] { streamCol, TenantIdColumn.Name },
                LinkedNames = new[] { "id", TenantIdColumn.Name },
                LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, StreamsTable.TableName),
                OnDelete = Weasel.Postgresql.CascadeAction.Cascade
            });
        }
        else
        {
            // FK to mt_streams with CASCADE delete
            ForeignKeys.Add(new ForeignKey(PostgresqlIdentifier.Shorten($"fk_{Identifier.Name}_stream"))
            {
                ColumnNames = new[] { streamCol },
                LinkedNames = new[] { "id" },
                LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, StreamsTable.TableName),
                OnDelete = Weasel.Postgresql.CascadeAction.Cascade
            });
        }

        // Index on stream id/key for reverse lookups
        Indexes.Add(new IndexDefinition(PostgresqlIdentifier.Shorten($"idx_{Identifier.Name}_{streamCol}"))
        {
            IsUnique = false,
            Columns = new[] { streamCol }
        });
    }

    /// <summary>
    /// Explicit ISchemaObject implementation to make FK creation idempotent.
    /// When CREATE TABLE IF NOT EXISTS is a no-op (table already exists from a prior
    /// test or concurrent migration), the base Table.WriteCreateStatement generates
    /// ALTER TABLE ADD CONSTRAINT which fails with "constraint already exists".
    /// This wraps each FK in a DO $$ IF NOT EXISTS guard.
    /// </summary>
    void ISchemaObject.WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        // Write the CREATE TABLE portion
        if (migrator.TableCreation == CreationStyle.DropThenCreate)
        {
            writer.WriteLine("DROP TABLE IF EXISTS {0} CASCADE;", Identifier);
            writer.WriteLine("CREATE TABLE {0} (", Identifier);
        }
        else
        {
            writer.WriteLine("CREATE TABLE IF NOT EXISTS {0} (", Identifier);
        }

        var lines = Columns
            .Select(column => column.ToDeclaration())
            .ToList();

        if (PrimaryKeyColumns.Any())
        {
            lines.Add($"CONSTRAINT {PrimaryKeyName} PRIMARY KEY ({string.Join(", ", PrimaryKeyColumns)})");
        }

        for (var i = 0; i < lines.Count - 1; i++)
        {
            writer.WriteLine(lines[i] + ",");
        }
        writer.WriteLine(lines.Last());

        if (Partitioning != null)
        {
            Partitioning.WritePartitionBy(writer);
        }
        else
        {
            writer.WriteLine(");");
        }

        // Write FKs with idempotent guard to avoid "constraint already exists"
        foreach (var foreignKey in ForeignKeys)
        {
            writer.WriteLine();
            writer.WriteLine("DO $$ BEGIN");
            writer.Write("IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = '");
            writer.Write(foreignKey.Name);
            writer.WriteLine("') THEN");
            writer.WriteLine(foreignKey.ToDDL(this));
            writer.WriteLine("END IF;");
            writer.WriteLine("END $$;");
        }

        // Write indexes with IF NOT EXISTS for idempotency
        foreach (var index in Indexes)
        {
            writer.WriteLine();
            var indexDdl = index.ToDDL(this);
            // Inject IF NOT EXISTS into CREATE INDEX statement
            indexDdl = indexDdl.Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ");
            indexDdl = indexDdl.Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ");
            writer.WriteLine(indexDdl);
        }

        if (Partitioning != null)
        {
            writer.WriteLine();
            Partitioning.WriteCreateStatement(writer, this);
        }
    }
}
