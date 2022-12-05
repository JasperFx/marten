using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

public class Bug_255_Hilo_table_being_erroneously_recreated : BugIntegrationContext
{
    [Fact]
    public void only_generated_once_default_connection_string_schema()
    {
        var logger = new DdlLogger();

        StoreOptions(opts =>
        {
            opts.Logger(logger);
        });

        theSession.Store(new IntDoc());
        theSession.SaveChanges();


        using (var store2 = SeparateStore(_ =>
               {
                   _.Logger(logger);
               }))
        {
            using (var session = store2.OpenSession())
            {
                session.Store(new IntDoc());
                session.SaveChanges();
            }
        }

        logger.Sql.Each(x => Console.WriteLine(x));

        logger.Sql.Where(x => x.Contains("mt_hilo") && x.Contains("CREATE TABLE")).Count()
            .ShouldBe(1);
    }
}

public class DdlLogger : IMartenLogger
{
    public readonly IList<string> Sql = new List<string>();

    public IMartenSessionLogger StartSession(IQuerySession session)
    {
        return new NulloMartenLogger();
    }

    public void SchemaChange(string sql)
    {
        Sql.Add(sql);
    }
}
