using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Marten.Schema;
using Marten.Util;

namespace Marten.Storage
{
    public interface ISchemaObject
    {
        void Write(DdlRules rules, StringWriter writer);
        void WriteDropStatement(DdlRules rules, StringWriter writer);

        DbObjectName Identifier { get; }
        void ConfigureQueryCommand(CommandBuilder builder);

        SchemaPatchDifference CreatePatch(DbDataReader reader, SchemaPatch patch, AutoCreate autoCreate);

        IEnumerable<DbObjectName> AllNames();
    }
}