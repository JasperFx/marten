using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Storage
{
    public class FunctionBodyTests
    {
        private string theFunctionBody = @"
CREATE OR REPLACE FUNCTION public.mt_upsert_target(
    doc jsonb,
    docdotnettype character varying,
    docid uuid,
    docversion uuid)
  RETURNS uuid AS
$BODY$
DECLARE
  final_version uuid;
BEGIN
INSERT INTO public.mt_doc_target (""data"", ""mt_dotnet_type"", ""id"", ""mt_version"", mt_last_modified) VALUES (doc, docDotNetType, docId, docVersion, transaction_timestamp())
  ON CONFLICT ON CONSTRAINT pk_mt_doc_target
  DO UPDATE SET ""data"" = doc, ""mt_dotnet_type"" = docDotNetType, ""mt_version"" = docVersion, mt_last_modified = transaction_timestamp();

  SELECT mt_version FROM public.mt_doc_target into final_version WHERE id = docId;
  RETURN final_version;
END;
$BODY$

";

        [Fact]
        public void derive_the_function_signature_from_the_body()
        {


            var func = new FunctionBody(new DbObjectName("public", "mt_upsert_target"), new string[0], theFunctionBody);

            func.Signature().ShouldBe("public.mt_upsert_target(jsonb, character varying, uuid, uuid)");
        }

        [Fact]
        public void do_substitutions()
        {
            var func = new FunctionBody(new DbObjectName("public", "mt_upsert_target"), new string[0], theFunctionBody);

            func.BuildTemplate($"*{DdlRules.SCHEMA}*").ShouldBe("*public*");
            func.BuildTemplate($"*{DdlRules.FUNCTION}*").ShouldBe("*mt_upsert_target*");
            func.BuildTemplate($"*{DdlRules.SIGNATURE}*").ShouldBe($"*{func.Signature()}*");
        }
    }
}