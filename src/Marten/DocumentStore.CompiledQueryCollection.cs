using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Baseline.ImTools;
using LamarCodeGeneration;
using LamarCompiler;
using Marten.Exceptions;
using Marten.Internal.CompiledQueries;
using Marten.Internal.Sessions;
using Marten.Linq;

namespace Marten
{
    public partial class DocumentStore : IGeneratesCode
    {
        // TODO -- will become more later
        private ImHashMap<Type, ICompiledQuerySource> _querySources = ImHashMap<Type, ICompiledQuerySource>.Empty;

        IReadOnlyList<ICodeFile> IGeneratesCode.BuildFiles()
        {
            // TODO -- will be more later

            return Options.CompiledQueryTypes.Select(x => new CompiledQueryCodeFile(x, this)).ToList();
        }

        string IGeneratesCode.ChildNamespace { get; } = "CompiledQueries";

        internal ICompiledQuerySource GetCompiledQuerySourceFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query,
            QuerySession session)
        {
            if (_querySources.TryFind(query.GetType(), out var source))
            {
                return source;
            }

            if (typeof(TOut).CanBeCastTo<Task>())
            {
                throw InvalidCompiledQueryException.ForCannotBeAsync(query.GetType());
            }


            var plan = QueryCompiler.BuildPlan(session, query, Options);
            var file = new CompiledQueryCodeFile(query.GetType(), this, plan);

            var rules = Options.CreateGenerationRules();
            rules.ReferenceTypes(typeof(TDoc), typeof(TOut), query.GetType());

            file.InitializeSynchronously(rules, this, null);

            source = file.Build(rules);
            _querySources = _querySources.AddOrUpdate(query.GetType(), source);

            return source;
        }
    }
}
