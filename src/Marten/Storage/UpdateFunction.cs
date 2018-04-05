using System.IO;
using Marten.Schema;

namespace Marten.Storage
{
    public class UpdateFunction : UpsertFunction
    {
        public UpdateFunction(DocumentMapping mapping) : base(mapping, mapping.UpdateFunction)
        {
        }

        protected override void writeFunction(StringWriter writer, string argList, string securityDeclaration, string inserts, string valueList,
            string updates)
        {
            var statement = updates.Contains("where")
                ? $"UPDATE {_tableName} SET {updates} and id = docId;"
                : $"UPDATE {_tableName} SET {updates} where id = docId;";

            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql {
                    securityDeclaration
                } AS $function$
DECLARE
  final_version uuid;
BEGIN
  {statement}

  SELECT mt_version FROM {_tableName} into final_version WHERE id = docId {_andTenantWhereClause};
  RETURN final_version;
END;
$function$;
");
        }
    }
}