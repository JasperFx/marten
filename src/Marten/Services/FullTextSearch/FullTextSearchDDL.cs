using System.IO;
using System.Linq;
using Marten.Schema;

namespace Marten.Services.FullTextSearch
{
    public sealed class FullTextSearchDDL
    {
        private readonly SearchMap map;
        private readonly IDocumentMapping mapping;

        public FullTextSearchDDL(SearchMap map, IDocumentMapping mapping)
        {
            this.map = map;
            this.mapping = mapping;
        }

        public void Write(StringWriter writer)
        {
            var add_column_if_not_exists =
$@"DO
$$
BEGIN
IF not EXISTS (SELECT null
               FROM information_schema.columns
               WHERE table_schema='{mapping.Table.Schema}' and table_name='{mapping.Table.Name}' and column_name='{map.VectorName}') THEN
alter table {mapping.Table.Name} add column {map.VectorName} tsvector default null ;
END IF;
END;
$$;";

            writer.WriteLine(add_column_if_not_exists);
            writer.WriteLine();

            var vector = string.Join(" || ", map.GetSearchables().Select(x => $"to_tsvector(NEW.data ->> '{x}')"));

            var create_trigger_factory =

$@"
create or replace function {mapping.Table.Schema}.{mapping.Table.Name}_update_{map.VectorName}() returns trigger as $make_doc_searchable$
begin
    NEW.{map.VectorName} := {vector};
    return NEW;
end;
$make_doc_searchable$ language plpgsql;";

            writer.WriteLine(create_trigger_factory);
            writer.WriteLine();

            var create_trigger_if_not_exists =
$@"drop trigger if exists {mapping.Table.Name}_update_{map.VectorName} on {mapping.Table.Schema}.{mapping.Table.Name};
create trigger {mapping.Table.Name}_update_{map.VectorName} before insert or update on {mapping.Table.Schema}.{mapping.Table.Name}
    for each row execute procedure {mapping.Table.Name}_update_{map.VectorName}();";

            writer.WriteLine(create_trigger_if_not_exists);
            writer.WriteLine();
        }
    }
}