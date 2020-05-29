using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Linq.Model
{
    public class BetterLinqQuery<T>: ILinqQuery
    {
        private readonly DocumentStore _store;


        private DocumentStatement statement;

        public BetterLinqQuery(DocumentStore store, QueryModel model)
        {
            Model = model;
            _store = store;

            //statement = new DocumentStatement();


            // TODO -- going to have to push in the ITenant eventually
            var mapping = store.Tenancy.Default.MappingFor(model.SourceType()).ToQueryableDocument();



            foreach (var clause in model.BodyClauses)
            {
                switch (clause)
                {
                    case WhereClause where:
                        //currentStatement.WhereClauses.Add(where);
                        break;
                    case OrderByClause orderBy:
                        //currentStatement.Orderings.AddRange(orderBy.Orderings);
                        break;
                    case AdditionalFromClause additional:
                        throw new NotImplementedException("Not yet handling SelectMany()");
                        break;
                    default:
                        throw new NotSupportedException();
                }
            }
        }


        public QueryModel Model { get; }
        public Type SourceType => Model.SourceType();




        public void ConfigureCommand(CommandBuilder command)
        {
            ConfigureCommand(command, 0);
        }

        public void ConfigureCommand(CommandBuilder command, int limit)
        {
            // TODO -- CommonTableExpression might need to be a double linked list

            // First, "bake" the Where clauses
            // TODO -- this is going to have to be fancier to deal with
            // SubQueryExpressions later
            // _mainStatement.CompileStructure(_store.Parser);
            //
            // // Hokey to set this, but we'll see
            // _mainStatement.RecordLimit = limit;
            // _mainStatement.Configure(command);
        }

        public void ConfigureCount(CommandBuilder command)
        {
            throw new NotImplementedException();
        }

        public void ConfigureAny(CommandBuilder command)
        {
            throw new NotImplementedException();
        }

        public void ConfigureAggregate(CommandBuilder command, string @operator)
        {
            throw new NotImplementedException();
        }
    }
}
