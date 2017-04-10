using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Generation
{
    public class TableDefinition
    {
        private string primaryKeyDirective => $"CONSTRAINT pk_{Name.Name} PRIMARY KEY";

        public TableName Name { get; }

        public IList<TableColumn> Columns { get; } = new List<TableColumn>();

        public TableColumn PrimaryKey
        {
            get { return Columns.FirstOrDefault(c => c.Directive == primaryKeyDirective); }
            private set
            {
                if(value == null) throw new ArgumentNullException(nameof(value));
                value.Directive = primaryKeyDirective;
                if (!Columns.Contains(value)) Columns.Add(value);
            }
        }


        public TableDefinition(TableName name, TableColumn primaryKey)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (primaryKey == null) throw new ArgumentNullException(nameof(primaryKey));

            Name = name;
            PrimaryKey = primaryKey;
        }

        public TableDefinition(TableName name, string pkName, IEnumerable<TableColumn> columns)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(pkName)) throw new ArgumentOutOfRangeException(nameof(pkName));

            Name = name;
            Columns.AddRange(columns);

            var primaryKey = Column(pkName);
            if (primaryKey == null) throw new InvalidOperationException($"Primary key {pkName} not found in columns.");
            PrimaryKey = primaryKey;
        }

        public string ToDDL(DdlRules rules)
        {
            var writer = new StringWriter();

            Write(rules, writer);

            return writer.ToString();
        }

        public void Write(DdlRules rules, StringWriter writer)
        {

            if (rules.TableCreation == CreationStyle.DropThenCreate)
            {
                writer.WriteLine("DROP TABLE IF EXISTS {0} CASCADE;", Name.QualifiedName);
                writer.WriteLine("CREATE TABLE {0} (", Name.QualifiedName);
            }
            else
            {
                writer.WriteLine("CREATE TABLE IF NOT EXISTS {0} (", Name.QualifiedName);
            }

            var length = Columns.Select(x => x.Name.Length).Max() + 4;

            Columns.Each(col =>
            {
                writer.Write($"    {col.ToDeclaration(length)}");
                if (col == Columns.Last())
                {
                    writer.WriteLine();
                }
                else
                {
                    writer.WriteLine(",");
                }
            });

            writer.WriteLine(");");

            writer.WriteLine(this.OriginStatement());
        }

        public TableColumn Column(string name)
        {
            return Columns.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public void ReplaceOrAddColumn(string name, string type, string directive = null)
        {
            var column = new TableColumn(name, type) { Directive = directive };
            var columnIndex = Columns.ToList().FindIndex(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (columnIndex >= 0)
            {
                Columns[columnIndex] = column;
            }
            else Columns.Add(column);
        }

        protected bool Equals(TableDefinition other)
        {
            return Columns.OrderBy(x => x.Name).SequenceEqual(other.Columns.OrderBy(x => x.Name)) && Equals(PrimaryKey, other.PrimaryKey) && Name.Equals(other.Name);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((TableDefinition)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Columns != null ? Columns.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (PrimaryKey != null ? PrimaryKey.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Name != null ? Name.GetHashCode() : 0);
                return hashCode;
            }
        }

        public bool HasColumn(string name)
        {
            return Columns.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public void RemoveColumn(string columnName)
        {
            Columns.RemoveAll(col => col.Name.EqualsIgnoreCase(columnName));
        }

        public string BuildTemplate(string template)
        {
            return template
                .Replace(DdlRules.SCHEMA, Name.Schema)
                .Replace(DdlRules.TABLENAME, Name.Name)
                .Replace(DdlRules.COLUMNS, Columns.Select(x => x.Name).Join(", "))
                .Replace(DdlRules.NON_ID_COLUMNS, Columns.Where(x => !x.Name.EqualsIgnoreCase("id")).Select(x => x.Name).Join(", "))
                .Replace(DdlRules.METADATA_COLUMNS, "mt_last_modified, mt_version, mt_dotnet_type");
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
    }
}