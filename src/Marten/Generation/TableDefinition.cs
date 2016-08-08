using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;

namespace Marten.Generation
{
    public class TableDefinition
    {
        private string primaryKeyDirective => $"CONSTRAINT pk_{Table.Name} PRIMARY KEY";

        public TableName Table { get; }

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

        public TableDefinition(TableName table, TableColumn primaryKey)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (primaryKey == null) throw new ArgumentNullException(nameof(primaryKey));

            Table = table;
            PrimaryKey = primaryKey;
        }

        public TableDefinition(TableName table, string pkName, IEnumerable<TableColumn> columns)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (string.IsNullOrEmpty(pkName)) throw new ArgumentOutOfRangeException(nameof(pkName));

            Table = table;
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
                writer.WriteLine("DROP TABLE IF EXISTS {0} CASCADE;", Table.QualifiedName);
                writer.WriteLine("CREATE TABLE {0} (", Table.QualifiedName);
            }
            else
            {
                writer.WriteLine("CREATE TABLE IF NOT EXISTS {0} (", Table.QualifiedName);
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

            rules.GrantToRoles.Each(role =>
            {
                writer.WriteLine($"GRANT SELECT ({Columns.Select(x => x.Name).Join(", ")}) ON TABLE {Table.QualifiedName} TO \"{role}\";");
            });
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
            return Columns.OrderBy(x => x.Name).SequenceEqual(other.Columns.OrderBy(x => x.Name)) && Equals(PrimaryKey, other.PrimaryKey) && Table.Equals(other.Table);
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
                hashCode = (hashCode * 397) ^ (Table != null ? Table.GetHashCode() : 0);
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
    }
}