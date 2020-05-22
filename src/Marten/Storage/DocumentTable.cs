using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Util;

namespace Marten.Storage
{
    public class DocumentTable: Table
    {
        public DocumentTable(DocumentMapping mapping) : base(mapping.Table)
        {
            // validate to ensure document has an Identity field or property
            mapping.Validate();

            var pgIdType = TypeMappings.GetPgType(mapping.IdMember.GetMemberType(), mapping.EnumStorage);
            var pgTextType = TypeMappings.GetPgType(string.Empty.GetType(), mapping.EnumStorage);

            var idColumn = new TableColumn("id", pgIdType);
            if (mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                AddPrimaryKeys(new List<TableColumn>
                {
                    idColumn,
                    new TenantIdColumn()
                });

                Indexes.Add(new IndexDefinition(mapping, TenantIdColumn.Name));
            }
            else
            {
                AddPrimaryKey(idColumn);
            }

            AddColumn("data", "jsonb", "NOT NULL");

            AddColumn<LastModifiedColumn>();
            AddColumn<VersionColumn>();
            AddColumn<DotNetTypeColumn>();

            foreach (var field in mapping.DuplicatedFields)
            {
                AddColumn(new DuplicatedFieldColumn(field));
            }

            if (mapping.IsHierarchy())
            {
                AddColumn(new DocumentTypeColumn(mapping));
            }

            if (mapping.DeleteStyle == DeleteStyle.SoftDelete)
            {
                AddColumn<DeletedColumn>();
                Indexes.Add(new IndexDefinition(mapping, DocumentMapping.DeletedColumn));
                AddColumn<DeletedAtColumn>();
            }

            Indexes.AddRange(mapping.Indexes);
            ForeignKeys.AddRange(mapping.ForeignKeys);
        }

        public string BuildTemplate(string template)
        {
            return template
                .Replace(DdlRules.SCHEMA, Identifier.Schema)
                .Replace(DdlRules.TABLENAME, Identifier.Name)
                .Replace(DdlRules.COLUMNS, _columns.Select(x => x.Name).Join(", "))
                .Replace(DdlRules.NON_ID_COLUMNS, _columns.Where(x => !x.Name.EqualsIgnoreCase("id")).Select(x => x.Name).Join(", "))
                .Replace(DdlRules.METADATA_COLUMNS, _columns.OfType<SystemColumn>().Select(x => x.Name).Join(", "));
        }

        public void WriteTemplate(DdlTemplate template, StringWriter writer)
        {
            var text = template?.TableCreation;
            if (text.IsNotEmpty())
            {
                writer.WriteLine();
                writer.WriteLine(BuildTemplate(text));
            }
        }

        protected bool Equals(DocumentTable other)
        {
            return base.Equals((Table)other);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != this.GetType())
                return false;
            return Equals((DocumentTable)obj);
        }

        public override int GetHashCode()
        {
            throw new System.NotImplementedException();
        }
    }

    public abstract class SystemColumn: TableColumn
    {
        protected SystemColumn(string name, string type) : base(name, type)
        {
        }
    }

    public class TenantIdColumn: SystemColumn
    {
        public new static readonly string Name = "tenant_id";

        public TenantIdColumn() : base(Name, "varchar")
        {
            CanAdd = true;
            Directive = $"DEFAULT '{Tenancy.DefaultTenantId}'";
        }
    }

    public class DeletedColumn: SystemColumn
    {
        public DeletedColumn() : base(DocumentMapping.DeletedColumn, "boolean")
        {
            Directive = "DEFAULT FALSE";
            CanAdd = true;
        }
    }

    public class DeletedAtColumn: SystemColumn
    {
        public DeletedAtColumn() : base(DocumentMapping.DeletedAtColumn, "timestamp with time zone")
        {
            CanAdd = true;
            Directive = "NULL";
        }
    }

    public class DocumentTypeColumn: SystemColumn
    {
        public DocumentTypeColumn(DocumentMapping mapping) : base(DocumentMapping.DocumentTypeColumn, "varchar")
        {
            CanAdd = true;
            Directive = $"DEFAULT '{mapping.AliasFor(mapping.DocumentType)}'";
            mapping.AddIndex(DocumentMapping.DocumentTypeColumn);
        }
    }

    public class LastModifiedColumn: SystemColumn
    {
        public LastModifiedColumn() : base(DocumentMapping.LastModifiedColumn, "timestamp with time zone")
        {
            Directive = "DEFAULT transaction_timestamp()";
            CanAdd = true;
        }
    }

    public class VersionColumn: SystemColumn
    {
        public VersionColumn() : base(DocumentMapping.VersionColumn, "uuid")
        {
            Directive = "NOT NULL default(md5(random()::text || clock_timestamp()::text)::uuid)";
            CanAdd = true;
        }
    }

    public class DotNetTypeColumn: SystemColumn
    {
        public DotNetTypeColumn() : base(DocumentMapping.DotNetTypeColumn, "varchar")
        {
            CanAdd = true;
        }
    }

    public class DuplicatedFieldColumn: TableColumn
    {
        private readonly DuplicatedField _field;
        private const string NullConstraint = "NULL";
        private const string NotNullConstraint = "NOT NULL";


        public DuplicatedFieldColumn(DuplicatedField field) : base(field.ColumnName, field.PgType, field.NotNull ? NotNullConstraint : NullConstraint)
        {
            CanAdd = true;
            _field = field;
        }

        public override string AddColumnSql(Table table)
        {
            return $"{base.AddColumnSql(table)}update {table.Identifier} set {_field.UpdateSqlFragment()};";
        }
    }
}
