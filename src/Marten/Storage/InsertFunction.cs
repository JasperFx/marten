using System.IO;
using Marten.Schema;
using Marten.Schema.Arguments;

namespace Marten.Storage
{
    public class InsertFunction: UpsertFunction
    {
        public InsertFunction(DocumentMapping mapping) : base(mapping, mapping.InsertFunction)
        {
        }

        protected override void writeFunction(StringWriter writer, string argList, string securityDeclaration, string inserts, string valueList,
            string updates)
        {
            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql {
                    securityDeclaration
                } AS $function$
BEGIN
INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList});

  RETURN {VersionArgument.ArgName};
END;
$function$;
");
        }
    }
}
