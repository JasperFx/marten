using System.IO;
using Marten.Schema;

namespace Marten.Storage;

internal class OverwriteFunction: UpsertFunction
{
    public OverwriteFunction(DocumentMapping mapping): base(mapping, mapping.OverwriteFunction, true)
    {
    }

    protected override void writeFunction(TextWriter writer, string argList, string securityDeclaration,
        string inserts, string valueList,
        string updates)
    {
        if (_mapping.Metadata.Revision.Enabled)
        {
            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS INTEGER LANGUAGE plpgsql {
    securityDeclaration
} AS $function$
DECLARE
  final_version INTEGER;
  current_version INTEGER;
BEGIN

  if revision = 0 then
    SELECT mt_version FROM {_tableName.QualifiedName} into current_version WHERE id = docId {_andTenantWhereClause};
    if current_version is not null then
      revision = current_version + 1;
    else
      revision = 1;
    end if;
  end if;

  INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})
  ON CONFLICT ({_primaryKeyFields})
  DO UPDATE SET {updates};

  SELECT mt_version FROM {_tableName.QualifiedName} into final_version WHERE id = docId {_andTenantWhereClause};
  RETURN final_version;
END;
$function$;
");
        }
        else
        {
            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql {
    securityDeclaration
} AS $function$
DECLARE
  final_version uuid;
BEGIN
INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})
  ON CONFLICT ({_primaryKeyFields})
  DO UPDATE SET {updates};

  SELECT mt_version FROM {_tableName.QualifiedName} into final_version WHERE id = docId {_andTenantWhereClause};
  RETURN final_version;
END;
$function$;
");
        }


    }
}
