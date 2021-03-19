using System;
using System.Collections.Generic;
using Marten.Internal;
using Marten.Internal.Operations;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Governs the advanced behavior of a projection shard running
    /// in the projection daemon
    /// </summary>
    public class AsyncOptions
    {
        /// <summary>
        /// The maximum range of events fetched at one time
        /// </summary>
        public int BatchSize { get; set; } = 500;

        /// <summary>
        /// The maximum number of events to be held in memory in preparation
        /// for determining projection updates.
        /// </summary>
        public int MaximumHopperSize { get; set; } = 5000;

        // TODO -- add an option to just use SQL

        /// <summary>
        /// Add explicit teardown rule to delete all documents of type T
        /// when this projection shard is rebuilt
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void DeleteViewTypeOnTeardown<T>()
        {
            DeleteViewTypeOnTeardown(typeof(T));
        }

        /// <summary>
        /// Add explicit teardown rule to delete all documents of type T
        /// when this projection shard is rebuilt
        /// </summary>
        /// <param name="type"></param>
        public void DeleteViewTypeOnTeardown(Type type)
        {
            _actions.Add(x => x.QueueOperation(new TruncateTable(type)));
            StorageTypes.Add(type);
        }

        private readonly IList<Action<IDocumentOperations>> _actions = new List<Action<IDocumentOperations>>();


        internal void Teardown(IDocumentOperations operations)
        {
            foreach (var action in _actions)
            {
                action(operations);
            }
        }

        /// <summary>
        /// Optional list of stored document or feature types that this projection
        /// writes. This is used by Marten to help build out schema objects if the
        /// async daemon is started before the rest of the application.
        /// </summary>
        public IList<Type> StorageTypes { get; } = new List<Type>();
    }


}
