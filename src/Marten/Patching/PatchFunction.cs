using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Functions;

namespace Marten.Patching;

public class PatchFunction: Function
{
    public const string Name = "mt_jsonb_patch";
    public string Body { get; set; }

    public PatchFunction(StoreOptions options, string body)
        : base(new PostgresqlObjectName(options.DatabaseSchemaName, Name))
    {
        Body = $"{body}";
    }
}
