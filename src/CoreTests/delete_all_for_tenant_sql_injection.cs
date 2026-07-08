using Marten.Internal.Operations;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

// Security regression: DeleteAllForTenant is reachable from projection-progress teardown
// (IEventStore.DeleteProjectionProgressAsync -> TeardownForTenant) with a tenant id that
// is NOT guaranteed to have passed AssertValidPostgresqlIdentifiers. It must bind the
// tenant id as a parameter, never interpolate it into the DELETE literal — otherwise a
// crafted tenant id yields an always-true predicate that deletes every tenant's rows.
public class delete_all_for_tenant_sql_injection
{
    [Fact]
    public void tenant_id_is_bound_as_a_parameter_not_interpolated()
    {
        var maliciousTenant = "x' or '1'='1";

        // qualifiedTableName constructor never dereferences the session in ConfigureCommand.
        var op = new DeleteAllForTenant("public.mt_doc_user", maliciousTenant);

        var builder = new CommandBuilder();
        op.ConfigureCommand(builder, null);
        var cmd = builder.Compile();

        // The malicious value must not appear inline in the SQL text...
        cmd.CommandText.ShouldNotContain("'1'='1");
        cmd.CommandText.ShouldNotContain("or '1'");

        // ...it must travel as a bound parameter carrying the exact literal value.
        cmd.Parameters.Count.ShouldBe(1);
        cmd.Parameters[0].Value.ShouldBe(maliciousTenant);
    }
}
