using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    public class DotNetTypeArgument: UpsertArgument
    {
        private static readonly MethodInfo _getType = typeof(object).GetMethod("GetType");

        private static readonly MethodInfo _fullName =
            ReflectionHelper.GetProperty<Type>(x => x.FullName).GetMethod;

        public DotNetTypeArgument()
        {
            Arg = "docDotNetType";
            Column = SchemaConstants.DotNetTypeColumn;
            DbType = NpgsqlDbType.Varchar;
            PostgresType = "varchar";
        }


        public override void GenerateCodeToSetDbParameterValue(GeneratedMethod method, GeneratedType type, int i, Argument parameters,
            DocumentMapping mapping, StoreOptions options)
        {
            var version = type.AllInjectedFields[0];

            method.Frames.Code("// .Net Class Type");
            method.Frames.Code("{0}[{1}].NpgsqlDbType = {2};", parameters, i, DbType);
            method.Frames.Code("{0}[{1}].Value = {2}.GetType().FullName;", parameters, i, version);
        }

        public override void GenerateBulkWriterCode(GeneratedType type, GeneratedMethod load, DocumentMapping mapping)
        {
            load.Frames.Code($"writer.Write(document.GetType().FullName, {{0}});", DbType);
        }
    }
}
