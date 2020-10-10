using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage
{
    internal class DocumentTable: Table
    {
        private readonly DocumentMapping _mapping;

        public DocumentTable(DocumentMapping mapping): base(mapping.TableName)
        {
            // validate to ensure document has an Identity field or property
            mapping.Validate();

            _mapping = mapping;

            var idColumn = new IdColumn(mapping);

            if (mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                AddPrimaryKeys(new List<TableColumn> {idColumn, mapping.Metadata.TenantId});

                Indexes.Add(new IndexDefinition(mapping, TenantIdColumn.Name));
            }
            else
            {
                AddPrimaryKey(idColumn);
            }

            AddColumn<DataColumn>();

            // TODO -- this is temporary!!!
            AddColumn(_mapping.Metadata.LastModified);
            AddColumn(_mapping.Metadata.Version);
            AddColumn(_mapping.Metadata.DotNetType);

            foreach (var field in mapping.DuplicatedFields) AddColumn(new DuplicatedFieldColumn(field));

            if (mapping.IsHierarchy())
            {
                Indexes.Add(new IndexDefinition(_mapping, SchemaConstants.DocumentTypeColumn));
                AddColumn(_mapping.Metadata.DocumentType);
            }

            if (mapping.DeleteStyle == DeleteStyle.SoftDelete)
            {
                AddColumn(_mapping.Metadata.IsSoftDeleted);
                Indexes.Add(new IndexDefinition(mapping, SchemaConstants.DeletedColumn));

                AddColumn(_mapping.Metadata.SoftDeletedAt);
            }

            Indexes.AddRange(mapping.Indexes);
            ForeignKeys.AddRange(mapping.ForeignKeys);
        }

        public string BuildTemplate(string template)
        {
            return template
                .Replace(DdlRules.SCHEMA, Identifier.Schema)
                .Replace(DdlRules.TABLENAME, Identifier.Name)
                .Replace(DdlRules.COLUMNS, Columns.Select(x => x.Name).Join(", "))
                .Replace(DdlRules.NON_ID_COLUMNS,
                    Columns.Where(x => !x.Name.EqualsIgnoreCase("id")).Select(x => x.Name).Join(", "))
                .Replace(DdlRules.METADATA_COLUMNS, Columns.OfType<MetadataColumn>().Select(x => x.Name).Join(", "));
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
            return base.Equals(other);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != GetType())
                return false;
            return Equals((DocumentTable)obj);
        }

        public override int GetHashCode()
        {
            return Identifier.QualifiedName.GetHashCode();
        }

        internal ISelectableColumn[] SelectColumns(StorageStyle style)
        {
            // There's some hokey stuff going here, but older code assumes that the
            // order of the selection is data, id, everything else
            var columns = Columns.OfType<ISelectableColumn>().Where(x => x.ShouldSelect(_mapping, style)).ToList();

            var id = columns.OfType<IdColumn>().SingleOrDefault();
            var data = columns.OfType<DataColumn>().Single();
            var type = columns.OfType<DocumentTypeColumn>().SingleOrDefault();
            var version = columns.OfType<VersionColumn>().SingleOrDefault();

            var answer = new List<ISelectableColumn>();

            if (id != null)
            {
                columns.Remove(id);
                answer.Add(id);
            }

            columns.Remove(data);
            answer.Add(data);

            if (type != null)
            {
                columns.Remove(type);

                // Old code might depend on this exact ordering
                answer.Add(type);
            }

            if (version != null)
            {
                columns.Remove(version);
                answer.Add(version);
            }

            answer.AddRange(columns);

            return answer.ToArray();
        }
    }
}
