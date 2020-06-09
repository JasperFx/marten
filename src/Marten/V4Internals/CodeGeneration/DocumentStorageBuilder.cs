using System;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Linq;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using ReflectionExtensions = LamarCodeGeneration.ReflectionExtensions;

namespace Marten.V4Internals
{
    public class DocumentStorageBuilder
    {
        private readonly DocumentMapping _mapping;
        private readonly StoreOptions _options;

        public DocumentStorageBuilder(DocumentMapping mapping, StoreOptions options)
        {
            _mapping = mapping;
            _options = options;
        }

        private class Operations
        {
            public GeneratedType Upsert { get; set; }
            public GeneratedType Insert { get; set; }
            public GeneratedType Update { get; set; }
            public GeneratedType Overwrite { get; set; }

            public GeneratedType DeleteById { get; set; }
            public GeneratedType DeleteByWhere { get; set; }
        }

        public StorageSlot<T> Generate<T>()
        {
            var assembly = new GeneratedAssembly(new GenerationRules("Marten.Generated"));

            var operations = new Operations
            {
                DeleteById = buildDeleteById(assembly),
                DeleteByWhere = buildDeleteForWhere(assembly),
                Upsert = new DocumentFunctionOperationBuilder(_mapping, new UpsertFunction(_mapping), StorageRole.Upsert).BuildType(assembly),
                Insert = new DocumentFunctionOperationBuilder(_mapping, new InsertFunction(_mapping), StorageRole.Insert).BuildType(assembly),
                Update = new DocumentFunctionOperationBuilder(_mapping, new UpdateFunction(_mapping), StorageRole.Update).BuildType(assembly)
            };

            if (_mapping.UseOptimisticConcurrency)
            {
                operations.Overwrite = new DocumentFunctionOperationBuilder(_mapping, new OverwriteFunction(_mapping), StorageRole.Update).BuildType(assembly);
            }


            var queryOnly = buildQueryOnlyStorage(assembly, operations);
            var lightweight = buildLightweightStorage(assembly, operations);
            var identityMap = buildIdentityMapStorage(assembly, operations);
            var dirtyTracking = buildDirtyTrackingStorage(assembly, operations);

            var compiler = new LamarCompiler.AssemblyGenerator();
            compiler.ReferenceAssembly(typeof(IDocumentStorage<>).Assembly);
            compiler.ReferenceAssembly(typeof(T).Assembly);

            compiler.Compile(assembly);

            var slot = new StorageSlot<T>
            {
                QueryOnly = (IDocumentStorage<T>)Activator.CreateInstance(queryOnly.CompiledType, _mapping),
                Lightweight = (IDocumentStorage<T>)Activator.CreateInstance(lightweight.CompiledType, _mapping),
                IdentityMap = (IDocumentStorage<T>)Activator.CreateInstance(identityMap.CompiledType, _mapping),
                DirtyTracking = (IDocumentStorage<T>)Activator.CreateInstance(dirtyTracking.CompiledType, _mapping)
            };

            return slot;
        }

        private GeneratedType buildDeleteForWhere(GeneratedAssembly assembly)
        {
            var baseType = typeof(DeleteMany<>).MakeGenericType(_mapping.DocumentType);
            var type = assembly.AddType($"Delete{_mapping.DocumentType.Name}ByWhere", baseType);

            var sql = $"delete from {_mapping.Table.QualifiedName} where ";
            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete)
            {
                sql = $"update {_mapping.Table.QualifiedName} as d set {DocumentMapping.DeletedColumn} = True, {DocumentMapping.DeletedAtColumn} = now() where ";
            }

            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                sql += $" d.{TenantIdColumn.Name} = ? and ";
            }


            var commandText = Setter.Constant("CommandText", Constant.For(sql));
            type.Setters.Add(commandText);

            var configure = type.MethodFor(nameof(IQueryHandler.ConfigureCommand));
            configure.Frames.Call<CommandBuilder>(x => x.AppendWithParameters(null), @call =>
            {
                @call.Arguments[0] = commandText;
            });

            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                configure.Frames.Code($@"
// tenant
{{0}}[0].NpgsqlDbType = {{1}};
{{0}}[0].Value = {{2}}.{nameof(IMartenSession.Tenant)}.{nameof(ITenant.TenantId)};
", Use.Type<NpgsqlParameter[]>(), NpgsqlDbType.Varchar, Use.Type<IMartenSession>());
            }

            configure.Frames.Code("{0}.Apply({1});", type.AllInjectedFields[0], Use.Type<CommandBuilder>());


            return type;
        }

        private GeneratedType buildDeleteById(GeneratedAssembly assembly)
        {
            var baseType = typeof(DeleteOne<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType($"Delete{_mapping.DocumentType.Name}ById", baseType);

            var sql = $"delete from {_mapping.Table.QualifiedName} as d where id = ?";
            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete)
            {
                sql = $"update {_mapping.Table.QualifiedName} as d set {DocumentMapping.DeletedColumn} = True, {DocumentMapping.DeletedAtColumn} = now() where id = ?";
            }

            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                sql += $" and d.{TenantIdColumn.Name} = ?";
            }


            var commandText = Setter.Constant("CommandText", Constant.For(sql));
            type.Setters.Add(commandText);

            var configure = type.MethodFor(nameof(IQueryHandler.ConfigureCommand));
            configure.Frames.Call<CommandBuilder>(x => x.AppendWithParameters(null), @call =>
            {
                @call.Arguments[0] = commandText;
            });

            // Add the Id parameter
            configure.Frames.Code(@"
// Id parameter
{0}[0].NpgsqlDbType = {1};
{0}[0].Value = {2};

", Use.Type<NpgsqlParameter[]>(), TypeMappings.ToDbType(_mapping.IdType), type.AllInjectedFields[0]);

            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                configure.Frames.Code($@"
// tenant
{{0}}[1].NpgsqlDbType = {{1}};
{{0}}[1].Value = {{2}}.{nameof(IMartenSession.Tenant)}.{nameof(ITenant.TenantId)};
", Use.Type<NpgsqlParameter[]>(), NpgsqlDbType.Varchar, Use.Type<IMartenSession>());
            }





            return type;
        }

        private GeneratedType buildDirtyTrackingStorage(GeneratedAssembly assembly, Operations operations)
        {

            var typeName = $"DirtyTracking{ReflectionExtensions.NameInCode(_mapping.DocumentType)}DocumentStorage";
            var baseType = typeof(DirtyTrackingDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);
            buildStorageOperationMethods(operations, type);
            writeNotImplementedStubs(type);

            return type;
        }

        private GeneratedType buildIdentityMapStorage(GeneratedAssembly assembly, Operations operations)
        {
            var typeName = $"IdentityMap{ReflectionExtensions.NameInCode(_mapping.DocumentType)}DocumentStorage";
            var baseType = typeof(IdentityMapDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);
            buildStorageOperationMethods(operations, type);
            writeNotImplementedStubs(type);

            return type;
        }

        private GeneratedType buildLightweightStorage(GeneratedAssembly assembly, Operations operations)
        {
            var typeName = $"Lightweight{ReflectionExtensions.NameInCode(_mapping.DocumentType)}DocumentStorage";
            var baseType = typeof(LightweightDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);

            buildStorageOperationMethods(operations, type);

            writeNotImplementedStubs(type);

            return type;
        }

        private GeneratedType buildQueryOnlyStorage(GeneratedAssembly assembly, Operations operations)
        {
            var typeName = $"QueryOnly{ReflectionExtensions.NameInCode(_mapping.DocumentType)}DocumentStorage";
            var baseType = typeof(QueryOnlyDocumentStorage<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(typeName, baseType);

            writeIdentityMethod(type);

            buildStorageOperationMethods(operations, type);


            writeNotImplementedStubs(type);

            return type;
        }

        private void buildStorageOperationMethods(Operations operations, GeneratedType type)
        {
            buildOperationMethod(type, operations, "Upsert");
            buildOperationMethod(type, operations, "Insert");
            buildOperationMethod(type, operations, "Update");

            if (_mapping.UseOptimisticConcurrency)
            {
                buildOperationMethod(type, operations, "Overwrite");
            }
            else
            {
                type.MethodFor("Overwrite").Frames.ThrowNotSupportedException();
            }

            type.MethodFor("DeleteForDocument").Frames.Code($@"
return new Marten.Generated.{operations.DeleteById.TypeName}(Identity({{0}}));
", new Use(_mapping.DocumentType));

            type.MethodFor("DeleteForId").Frames.Code($@"
return new Marten.Generated.{operations.DeleteById.TypeName}({{0}});
", new Use(_mapping.IdType));

            type.MethodFor("DeleteForWhere").Frames.Code($@"
return new Marten.Generated.{operations.DeleteByWhere.TypeName}({{0}});
", Use.Type<IWhereFragment>());

        }

        private void buildOperationMethod(GeneratedType type, Operations operations, string methodName)
        {
            var operationType = (GeneratedType)typeof(Operations).GetProperty(methodName).GetValue(operations);
            var method = type.MethodFor(methodName);

            method.Frames
                .Code($@"return new Marten.Generated.{operationType.TypeName}
    (
        {{0}}, Identity({{0}}),
        {{1}}.Versions.ForType<{ReflectionExtensions.FullNameInCode(_mapping.DocumentType)},
        {ReflectionExtensions.FullNameInCode(_mapping.IdType)}>()
    );", new Use(_mapping.DocumentType), Use.Type<IMartenSession>());
        }


        private void writeIdentityMethod(GeneratedType type)
        {
            var identity = type.MethodFor("Identity");
            identity.Frames.Code($"return {{0}}.{_mapping.IdMember.Name};", identity.Arguments[0]);
        }

        private static void writeNotImplementedStubs(GeneratedType type)
        {
            // var missing = type.Methods.Where(x => !x.Frames.Any()).Select(x => x.MethodName);
            // if (missing.Any())
            // {
            //     throw new Exception("Missing methods: " + missing.Join(", "));
            // }

            foreach (var method in type.Methods)
            {
                if (!method.Frames.Any())
                {
                    method.Frames.ThrowNotImplementedException();
                }
            }
        }
    }
}
