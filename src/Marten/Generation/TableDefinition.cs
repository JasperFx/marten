using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;

namespace Marten.Generation
{
    public class TableDefinition
    {
        public readonly IList<TableColumn> Columns = new List<TableColumn>();

        public TableColumn PrimaryKey;

        public TableDefinition(string name, TableColumn primaryKey)
        {
            Name = name;
            PrimaryKey = primaryKey;
            PrimaryKey.Directive = "CONSTRAINT pk_{0} PRIMARY KEY".ToFormat(name);

            Columns.Add(primaryKey);
        }

        public TableDefinition(string name, string pkName, IEnumerable<TableColumn> columns)
        {
            Name = name;
            Columns.AddRange(columns);

            PrimaryKey = columns.FirstOrDefault(x => x.Name == pkName);
            if (PrimaryKey != null)
            {
                PrimaryKey.Directive = "CONSTRAINT pk_{0} PRIMARY KEY".ToFormat(name);
            }
        }

        public string Name { get; set; }

        public void Write(StringWriter writer)
        {
            writer.WriteLine("DROP TABLE IF EXISTS {0} CASCADE;", Name);
            writer.WriteLine("CREATE TABLE {0} (", Name);

            var length = Columns.Select(x => x.Name.Length).Max() + 4;

            Columns.Each(col =>
            {
                writer.Write("    {0}{1} {2}", col.Name.PadRight(length), col.Type, col.Directive);
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
        }

        public TableColumn Column(string name)
        {
            return Columns.FirstOrDefault(x => x.Name == name);
        }
    }
}