using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using LamarCompiler;
using Marten.Internal.Storage;
using Marten.Linq.Includes;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;
using Marten.Schema.Arguments;
using Marten.Util;

namespace Marten.Internal.CompiledQueries
{
    public class CompiledQuerySourceBuilder
    {
        private readonly CompiledQueryPlan _plan;
        private readonly StoreOptions _storeOptions;

        public CompiledQuerySourceBuilder(CompiledQueryPlan plan, StoreOptions storeOptions)
        {
            _plan = plan;
            _storeOptions = storeOptions;
        }

        public ICompiledQuerySource Build()
        {
            var assembly = new GeneratedAssembly(new GenerationRules("Marten.Generated"));


            var handlerType = determineHandlerType();

            var hardcoded = new HardCodedParameters(_plan);
            var compiledHandlerType = buildHandlerType(assembly, handlerType, hardcoded);

            var sourceType = buildSourceType(assembly, handlerType, compiledHandlerType);

            assembly.Namespaces.Add("System");
            compileAssembly(assembly);

Debug.WriteLine(compiledHandlerType.SourceCode);

            return (ICompiledQuerySource)Activator.CreateInstance(sourceType.CompiledType, new object[] {hardcoded, _plan.HandlerPrototype});
        }

        private GeneratedType buildSourceType(GeneratedAssembly assembly, CompiledSourceType handlerType,
            GeneratedType compiledHandlerType)
        {
            var sourceBaseType = typeof(CompiledQuerySource<,>).MakeGenericType(_plan.OutputType, _plan.QueryType);
            var sourceName = _plan.QueryType.Name + "CompiledQuerySource";
            var sourceType = assembly.AddType(sourceName, sourceBaseType);

            var hardcoded = new InjectedField(typeof(HardCodedParameters), "hardcoded");
            sourceType.AllInjectedFields.Add(hardcoded);

            var buildHandler = sourceType.MethodFor("BuildHandler");
            switch (handlerType)
            {
                case CompiledSourceType.Cloneable:
                    var innerField = new InjectedField(typeof(IMaybeStatefulHandler));
                    sourceType.AllInjectedFields.Add(innerField);

                    var statistics = _plan.StatisticsMember == null ? "null" : $"query.{_plan.StatisticsMember.Name}";
                    buildHandler.Frames.Code(
                        $"return new Marten.Generated.{compiledHandlerType.TypeName}({innerField.Usage}, query, {statistics}, _hardcoded);");
                    break;

                case CompiledSourceType.Stateless:
                    var inner = new InjectedField(typeof(IQueryHandler<>).MakeGenericType(_plan.OutputType));
                    sourceType.AllInjectedFields.Add(inner);

                    buildHandler.Frames.Code(
                        $"return new Marten.Generated.{compiledHandlerType.TypeName}({inner.Usage}, query, _hardcoded);");
                    break;

                case CompiledSourceType.Complex:
                    var innerField2 = new InjectedField(typeof(IMaybeStatefulHandler));
                    sourceType.AllInjectedFields.Add(innerField2);

                    buildHandler.Frames.Code(
                        $"return new Marten.Generated.{compiledHandlerType.TypeName}({innerField2.Usage}, query, _hardcoded);");
                    break;
            }

            return sourceType;
        }

        private void compileAssembly(GeneratedAssembly assembly)
        {
            var compiler = new AssemblyGenerator();
            compiler.ReferenceAssembly(typeof(IDocumentStorage<>).Assembly);
            compiler.ReferenceAssembly(_plan.QueryType.Assembly);
            compiler.ReferenceAssembly(_plan.OutputType.Assembly);

            compiler.Compile(assembly);
        }

        private GeneratedType buildHandlerType(GeneratedAssembly assembly,
            CompiledSourceType handlerType, HardCodedParameters hardcoded)
        {
            var queryTypeName = _plan.QueryType.Name + "CompiledQuery";
            var baseType = determineBaseType(handlerType);
            var type = assembly.AddType(queryTypeName, baseType);


            configureCommandMethod(type, hardcoded);

            if (handlerType == CompiledSourceType.Complex)
            {
                buildHandlerMethod(type);
            }

            return type;
        }

        private void buildHandlerMethod(GeneratedType compiledType)
        {
            var method = compiledType.MethodFor("BuildHandler");

            var handlerName = "_inner";

            // first build out the inner
            if (_plan.HandlerPrototype is IMaybeStatefulHandler h && h.DependsOnDocumentSelector())
            {
                handlerName = "cloned";

                var statistics = _plan.StatisticsMember == null ? "null" : $"query.{_plan.StatisticsMember.Name}";

                method.Frames.Code(
                    $"var cloned = _inner.{nameof(IMaybeStatefulHandler.CloneForSession)}(session, {statistics});");
            }

            if (_plan.IncludeMembers.Any())
            {
                var readers = _plan.IncludeMembers.Select(buildIncludeReader);

                var includeHandlerType = typeof(IncludeQueryHandler<>).MakeGenericType(_plan.OutputType);

                var readerArray = "{{" + readers.Join(", ") + "}}";

                var constructorHandlerType = typeof(IQueryHandler<>).MakeGenericType(_plan.OutputType);

                method.Frames.Code(
                    $"var includeWriters = new {typeof(IIncludeReader).FullNameInCode()}[]{readerArray};");
                method.Frames.Code($"var included = new {includeHandlerType.FullNameInCode()}(({constructorHandlerType.FullNameInCode()}){handlerName}, includeWriters);");

                handlerName = "included";
            }

            method.Frames.Code($"return {handlerName};");
        }

        private string buildIncludeReader(MemberInfo member)
        {
            var memberType = member.GetMemberType();
            if (memberType.Closes(typeof(Action<>)))
            {
                return $"{typeof(Include).FullNameInCode()}.{nameof(Include.ReaderToAction)}(session, _query.{member.Name})";
            }

            if (memberType.Closes(typeof(IList<>)))
            {
                return $"{typeof(Include).FullNameInCode()}.{nameof(Include.ReaderToList)}(session, _query.{member.Name})";
            }

            if (memberType.Closes(typeof(IDictionary<,>)))
            {
                var includeType = memberType.GetGenericArguments().Last();
                var idType = memberType.GetGenericArguments().First();
                return $"{typeof(Include).FullNameInCode()}.{nameof(Include.ReaderToDictionary)}<{includeType.FullNameInCode()},{idType.FullNameInCode()}>(session, _query.{member.Name})";
            }

            throw new ArgumentOutOfRangeException();
        }


        private void configureCommandMethod(GeneratedType compiledType, HardCodedParameters hardcoded)
        {
            var method = compiledType.MethodFor(nameof(IQueryHandler.ConfigureCommand));
            method.Frames.Code($"var parameters = {{0}}.{nameof(CommandBuilder.AppendWithParameters)}(@{{1}});",
                Use.Type<CommandBuilder>(), _plan.CorrectedCommandText());

            foreach (var parameter in _plan.Parameters)
            {
                parameter.GenerateCode(method, _storeOptions);
            }

            if (hardcoded.HasTenantId)
            {
                method.Frames.Code($"{{0}}.{nameof(CommandBuilder.AddNamedParameter)}({{1}}, session.Tenant.TenantId);",
                    Use.Type<CommandBuilder>(), TenantIdArgument.ArgName);
            }

            if (hardcoded.HasAny())
            {
                method.Frames.Code($"_hardcoded.{nameof(HardCodedParameters.Apply)}(parameters);");
            }
        }

        private Type determineBaseType(CompiledSourceType sourceType)
        {
            switch (sourceType)
            {
                case CompiledSourceType.Cloneable:
                    return typeof(ClonedCompiledQuery<,>).MakeGenericType(_plan.OutputType, _plan.QueryType);

                case CompiledSourceType.Complex:
                    return typeof(ComplexCompiledQuery<,>).MakeGenericType(_plan.OutputType, _plan.QueryType);

                case CompiledSourceType.Stateless:
                    return typeof(StatelessCompiledQuery<,>).MakeGenericType(_plan.OutputType, _plan.QueryType);
            }

            throw new ArgumentOutOfRangeException();
        }

        private CompiledSourceType determineHandlerType()
        {
            if (_plan.IncludeMembers.Any())
            {
                return CompiledSourceType.Complex;
            }

            if (_plan.HandlerPrototype is IMaybeStatefulHandler h)
            {
                if (h.DependsOnDocumentSelector()) return CompiledSourceType.Cloneable;

            }

            return CompiledSourceType.Stateless;
        }
    }
}
