using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Internal.Linq;
using Marten.Internal.Operations;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.CodeGeneration
{
    internal class DeleteByIdBuilder
    {
        private readonly DocumentMapping _mapping;

        public DeleteByIdBuilder(DocumentMapping mapping)
        {
            _mapping = mapping;
        }

        public GeneratedType Build(GeneratedAssembly assembly)
        {
            var baseType = typeof(DeleteOne<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType($"Delete{_mapping.DocumentType.Name.Sanitize()}ById", baseType);

            var sql = $"delete from {_mapping.Table.QualifiedName} as d where id = ?";
            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete)
                sql =
                    $"update {_mapping.Table.QualifiedName} as d set {DocumentMapping.DeletedColumn} = True, {DocumentMapping.DeletedAtColumn} = now() where id = ?";

            if (_mapping.TenancyStyle == TenancyStyle.Conjoined) sql += $" and d.{TenantIdColumn.Name} = ?";


            var commandText = Setter.Constant("CommandText", Constant.For(sql));
            type.Setters.Add(commandText);

            var configure = type.MethodFor(nameof(IQueryHandler.ConfigureCommand));
            configure.Frames.Call<CommandBuilder>(x => x.AppendWithParameters(null), call =>
            {
                call.Arguments[0] = commandText;
            });

            // Add the Id parameter
            configure.Frames.Code(@"
// Id parameter
{0}[0].NpgsqlDbType = {1};
{0}[0].Value = {2};

", Use.Type<NpgsqlParameter[]>(), TypeMappings.ToDbType(_mapping.IdType), type.AllInjectedFields[0]);

            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
                configure.Frames.Code($@"
// tenant
{{0}}[1].NpgsqlDbType = {{1}};
{{0}}[1].Value = {{2}}.{nameof(IMartenSession.Tenant)}.{nameof(ITenant.TenantId)};
", Use.Type<NpgsqlParameter[]>(), NpgsqlDbType.Varchar, Use.Type<IMartenSession>());


            return type;
        }
    }
}
