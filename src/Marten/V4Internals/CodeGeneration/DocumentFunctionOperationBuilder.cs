using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Schema;
using Marten.Storage;
using TypeMappings = Marten.Util.TypeMappings;

namespace Marten.V4Internals
{
    public class DocumentFunctionOperationBuilder
    {
        private readonly UpsertFunction _function;
        private readonly DocumentMapping _mapping;
        private readonly StorageRole _role;

        public DocumentFunctionOperationBuilder(DocumentMapping mapping, UpsertFunction function, StorageRole role)
        {
            _function = function;
            _role = role;

            CommandText = $"select {_function.Identifier}({_function.OrderedArguments().Select(x => "?").Join(", ")})";

            ClassName =
                $"{function.GetType().Name.Replace("Function", "")}{mapping.DocumentType.NameInCode()}Operation";

            _mapping = mapping;
        }

        public string ClassName { get; }
        public string CommandText { get; }

        public GeneratedType BuildType(GeneratedAssembly assembly)
        {
            var baseType = typeof(StorageOperation<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(ClassName, baseType);

            if (_mapping.IsHierarchy())
            {
                type.AllInjectedFields.Add(new InjectedField(typeof(DocumentMapping), "mapping"));
            }

            type.MethodFor("Role").Frames.Return(Constant.ForEnum(_role));
            type.MethodFor("DbType").Frames.Return(Constant.ForEnum(TypeMappings.ToDbType(_mapping.IdType)));
            type.MethodFor("CommandText").Frames.Return(Constant.ForString(CommandText));

            buildConfigureMethod(type);

            buildPostprocessingMethods(type);

            return type;
        }

        private void buildPostprocessingMethods(GeneratedType type)
        {
            var sync = type.MethodFor(nameof(IStorageOperation.Postprocess));
            var @async = type.MethodFor(nameof(IStorageOperation.PostprocessAsync));

            void applyVersionToDocument()
            {
                if (_mapping.VersionMember != null)
                {
                    var code = $"_document.{_mapping.VersionMember.Name} = _version;";
                    sync.Frames.Code(code);
                    async.Frames.Code(code);
                }
            }

            if (_mapping.UseOptimisticConcurrency)
            {
                @async.AsyncMode = AsyncMode.AsyncTask;
                @async.Frames.CodeAsync("BLOCK:if (await postprocessConcurrencyAsync({0}, {1}, {2}))", Use.Type<DbDataReader>(), Use.Type<IList<Exception>>(), Use.Type<CancellationToken>());
                @sync.Frames.Code("BLOCK:if (postprocessConcurrency({0}, {1}))", Use.Type<DbDataReader>(), Use.Type<IList<Exception>>());


                applyVersionToDocument();

                @async.Frames.Code("END");
                @sync.Frames.Code("END");

            }
            else
            {
                sync.Frames.Code("storeVersion();");
                async.Frames.Code("storeVersion();");
                applyVersionToDocument();
            }


            @async.AsyncMode = AsyncMode.ReturnCompletedTask;
            @async.Frames.Add(new CommentFrame("Nothing"));
        }

        private void buildConfigureMethod(GeneratedType type)
        {
            var method = type.MethodFor("ConfigureParameters");
            var parameters = method.Arguments[0];

            var arguments = _function.OrderedArguments();
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                argument.GenerateCode(method, type, i, parameters);
            }
        }
    }

}
