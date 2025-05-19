using System;
using System.Linq;
using JasperFx;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_837_missing_func_mt_immutable_timestamp_when_initializing_with_new_Schema: BugIntegrationContext
{
    [Fact]
    public void missing_func_mt_immutable_timestamp_when_initializing_with_new_Schema()
    {
        var store = SeparateStore(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;
            _.DatabaseSchemaName = "other1";
        });

        using var session = store.QuerySession();
        session.Query<Target>().FirstOrDefault(m => m.DateOffset > DateTimeOffset.UtcNow)
            .ShouldBeNull();
    }


}
