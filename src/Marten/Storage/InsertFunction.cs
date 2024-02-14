using System;
using System.IO;
using Marten.Schema;
using Marten.Schema.Arguments;

namespace Marten.Storage;

internal class InsertFunction: UpsertFunction
{
    public InsertFunction(DocumentMapping mapping): base(mapping, mapping.InsertFunction)
    {
    }

    protected override void writeFunction(TextWriter writer, string argList, string securityDeclaration,
        string inserts, string valueList,
        string updates)
    {
        if (_mapping.UseNumericRevisions)
        {
            writeFunctionForIntRevision(writer, argList, securityDeclaration, inserts, valueList);
        }
        else
        {
            writeFunctionForGuidVersion(writer, argList, securityDeclaration, inserts, valueList);
        }
    }

    private void writeFunctionForGuidVersion(TextWriter writer, string argList, string securityDeclaration, string inserts,
        string valueList)
    {
        var versionArg = _mapping.Metadata.Version.Enabled
            ? VersionArgument.ArgName
            : $"'{Guid.Empty}'";

        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql {
    securityDeclaration
} AS $function$
BEGIN
INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList});

  RETURN {versionArg};
END;
$function$;
");
    }

    private void writeFunctionForIntRevision(TextWriter writer, string argList, string securityDeclaration, string inserts,
        string valueList)
    {

        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS INTEGER LANGUAGE plpgsql {
    securityDeclaration
} AS $function$
BEGIN
INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList});
  RETURN 1;
END;
$function$;
");
    }
}
