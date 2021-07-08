using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Internal.Operations;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using Weasel.Postgresql;

namespace Marten.Internal.CodeGeneration
{

    public static class StringExtensions
    {
        public static string Sanitize(this string value)
        {
            return Regex.Replace(value, @"[\#\<\>\,\.\]\[\`\+\-]", "_").Replace(" ", "");
        }
    }

    internal class DocumentFunctionOperationBuilder
    {
        private readonly UpsertFunction _function;
        private readonly DocumentMapping _mapping;
        private readonly OperationRole _role;
        private readonly StoreOptions _options;

        public DocumentFunctionOperationBuilder(DocumentMapping mapping, UpsertFunction function, OperationRole role,
            StoreOptions options)
        {
            _function = function;
            _role = role;
            _options = options;

            CommandText = $"select {_function.Identifier}({_function.OrderedArguments().Select(x => "?").Join(", ")})";

            ClassName =
                $"{function.GetType().Name.Replace("Function", "")}{mapping.DocumentType.Name.Sanitize()}Operation";

            _mapping = mapping;
        }

        public string ClassName { get; }
        public string CommandText { get; }

        public GeneratedType BuildType(GeneratedAssembly assembly)
        {
            var baseType = typeof(StorageOperation<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(ClassName, baseType);

            if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
            {
                type.AllInjectedFields.Add(new InjectedField(typeof(ITenant)));
            }

            type.MethodFor("Role").Frames.Return(Constant.ForEnum(_role));
            type.MethodFor("DbType").Frames.Return(Constant.ForEnum(PostgresqlProvider.Instance.ToParameterType(_mapping.IdType)));
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
                if (_mapping.Metadata.Version.Member != null)
                {
                    sync.Frames.SetMemberValue(_mapping.Metadata.Version.Member, "_version", _mapping.DocumentType, type);
                    async.Frames.SetMemberValue(_mapping.Metadata.Version.Member, "_version", _mapping.DocumentType, type);
                }
            }

            if (_mapping.UseOptimisticConcurrency)
            {
                @async.AsyncMode = AsyncMode.AsyncTask;
                @async.Frames.CodeAsync("BLOCK:if (await postprocessConcurrencyAsync({0}, {1}, {2}))",
                    Use.Type<DbDataReader>(), Use.Type<IList<Exception>>(), Use.Type<CancellationToken>());
                @sync.Frames.Code("BLOCK:if (postprocessConcurrency({0}, {1}))", Use.Type<DbDataReader>(),
                    Use.Type<IList<Exception>>());


                applyVersionToDocument();

                @async.Frames.Code("END");
                @sync.Frames.Code("END");

                return;
            }
            else
            {
                sync.Frames.Code("storeVersion();");
                async.Frames.Code("storeVersion();");
                applyVersionToDocument();

                if (_role == OperationRole.Update)
                {
                    @async.AsyncMode = AsyncMode.AsyncTask;

                    sync.Frames.Code("postprocessUpdate({0}, {1});", Use.Type<DbDataReader>(),
                        Use.Type<IList<Exception>>());
                    async.Frames.CodeAsync("await postprocessUpdateAsync({0}, {1}, {2});", Use.Type<DbDataReader>(),
                        Use.Type<IList<Exception>>(), Use.Type<CancellationToken>());
                }
                else
                {
                    @async.AsyncMode = AsyncMode.ReturnCompletedTask;
                    @async.Frames.Add(new CommentFrame("Nothing"));
                }
            }
        }

        private void buildConfigureMethod(GeneratedType type)
        {
            var method = type.MethodFor("ConfigureParameters");
            var parameters = method.Arguments[0];

            var arguments = _function.OrderedArguments();

            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                argument.GenerateCodeToModifyDocument(method, type, i, parameters, _mapping, _options);
            }

            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                argument.GenerateCodeToSetDbParameterValue(method, type, i, parameters, _mapping, _options);
            }
        }
    }

}
