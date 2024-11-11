using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Weasel.Core;

namespace Marten.Events.Daemon.Internals;

public static class AsyncOptionsExtensions
{

    /// <summary>
    ///     Add an explicit teardown rule to wipe data in the named table
    ///     when this projection shard is rebuilt
    /// </summary>
    /// <param name="name"></param>
    public static void DeleteDataInTableOnTeardown(this AsyncOptions options, DbObjectName name)
    {
        options.DeleteDataInTableOnTeardown(name.QualifiedName);
    }
}
