using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using ReflectionExtensions = LamarCodeGeneration.ReflectionExtensions;

namespace Marten.V4Internals
{
    public class DocumentFunctionOperationBuilder
    {
        private readonly DocumentMapping _mapping;
        private readonly UpsertFunction _function;
        private readonly StorageRole _role;

        public DocumentFunctionOperationBuilder(DocumentMapping mapping, UpsertFunction function, StorageRole role)
        {
            _function = function;
            _role = role;

            CommandText = $"select {_function.Identifier}({_function.Arguments.Select(x => "?").Join(", ")})";

            ClassName = $"{function.GetType().Name.Replace("Function", "")}{ReflectionExtensions.NameInCode(mapping.DocumentType)}Operation";

            _mapping = mapping;

        }

        public string ClassName { get; }
        public string CommandText { get; }

        public GeneratedType BuildType(GeneratedAssembly assembly)
        {
            var baseType = typeof(StorageOperation<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(ClassName, baseType);

            type.MethodFor("Role").Frames.Return(Constant.ForEnum(_role));
            type.MethodFor("DbType").Frames.Return(Constant.ForEnum(TypeMappings.ToDbType(_mapping.IdType)));
            type.MethodFor("CommandText").Frames.Return(Constant.ForString(CommandText));

            buildConfigureMethod(type);

            if (_mapping.UseOptimisticConcurrency)
            {
                throw new NotImplementedException("Not ready for this yet!");
            }
            else
            {
                type.MethodFor(nameof(IStorageOperation.Postprocess)).Frames.Add(new CommentFrame("Nothing"));
                var postprocessAsync = type.MethodFor(nameof(IStorageOperation.PostprocessAsync));
                postprocessAsync.AsyncMode = AsyncMode.ReturnCompletedTask;
                postprocessAsync.Frames.Add(new CommentFrame("Nothing"));
            }

            return type;
        }

        private void buildConfigureMethod(GeneratedType type)
        {
            var method = type.MethodFor("ConfigureParameters");
            var parameters = method.Arguments[0];

            for (int i = 2; i < _function.Arguments.Count; i++)
            {
                var argument = _function.Arguments[i];
                argument.GenerateCode(method, type, i, parameters);
            }


        }

    }

}
