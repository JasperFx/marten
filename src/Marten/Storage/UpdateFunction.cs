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
            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS bigint LANGUAGE plpgsql {
                    securityDeclaration
                } AS $function$
DECLARE
  final_version bigint;
  affected integer;
BEGIN
  UPDATE {_tableName} SET {updates};

  GET DIAGNOSTICS affected = ROW_COUNT;
  
  IF affected = 0 THEN
  	RETURN -1;
  END IF;
  
  SELECT mt_version FROM {_tableName} into final_version WHERE id = docId;
  RETURN final_version;
END;
$function$;
");
        }
    }
}