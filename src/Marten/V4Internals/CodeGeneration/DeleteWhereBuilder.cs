using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.V4Internals
{
    internal class DeleteWhereBuilder
    {
        private readonly DocumentMapping _mapping;

        public DeleteWhereBuilder(DocumentMapping mapping)
        {
            _mapping = mapping;
        }

        public GeneratedType Build(GeneratedAssembly assembly)
        {
            var baseType = typeof(DeleteMany<>).MakeGenericType(_mapping.DocumentType);
            var type = assembly.AddType($"Delete{_mapping.DocumentType.Name}ByWhere", baseType);

            var sql = $"delete from {_mapping.Table.QualifiedName} where ";
            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete)
                sql =
                    $"update {_mapping.Table.QualifiedName} as d set {DocumentMapping.DeletedColumn} = True, {DocumentMapping.DeletedAtColumn} = now() where ";

            if (_mapping.TenancyStyle == TenancyStyle.Conjoined) sql += $" d.{TenantIdColumn.Name} = ? and ";


            var commandText = Setter.Constant("CommandText", Constant.For(sql));
            type.Setters.Add(commandText);

            var configure = type.MethodFor(nameof(IQueryHandler.ConfigureCommand));
            configure.Frames.Call<CommandBuilder>(x => x.AppendWithParameters(null), call =>
            {
                call.Arguments[0] = commandText;
            });

            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
                configure.Frames.Code($@"
// tenant
{{0}}[0].NpgsqlDbType = {{1}};
{{0}}[0].Value = {{2}}.{nameof(IMartenSession.Tenant)}.{nameof(ITenant.TenantId)};
", Use.Type<NpgsqlParameter[]>(), NpgsqlDbType.Varchar, Use.Type<IMartenSession>());

            configure.Frames.Code("{0}.Apply({1});", type.AllInjectedFields[0], Use.Type<CommandBuilder>());


            return type;
        }
    }
}
