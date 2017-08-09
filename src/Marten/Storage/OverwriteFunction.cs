using System.IO;
using Marten.Schema;

namespace Marten.Storage
{
    public class OverwriteFunction : UpsertFunction
    {
        public OverwriteFunction(DocumentMapping mapping) : base(mapping, mapping.OverwriteFunction, true)
        {
        }

        protected override void writeFunction(StringWriter writer, string argList, string securityDeclaration, string inserts, string valueList,
            string updates)
        {
            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS bigint LANGUAGE plpgsql {
                    securityDeclaration
                } AS $function$
DECLARE
  final_version bigint;
BEGIN
  INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})
  ON CONFLICT ON CONSTRAINT {_primaryKeyConstraintName}
  DO UPDATE SET {updates};

  SELECT mt_version FROM {_tableName.QualifiedName} into final_version WHERE id = docId;
  RETURN final_version;
END;
$function$;
");
        }
    }
}