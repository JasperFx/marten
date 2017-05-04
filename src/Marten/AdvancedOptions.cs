using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Util;

namespace Marten
{
    public class AdvancedOptions
    {
        private readonly DocumentStore _store;
        private readonly CharArrayTextWriter.IPool _writerPool;

        public AdvancedOptions(DocumentStore store, IDocumentCleaner cleaner, CharArrayTextWriter.IPool writerPool)
        {
            _store = store;
            _writerPool = writerPool;
            Clean = cleaner;
        }

        /// <summary>
        ///     The original StoreOptions used to configure the current DocumentStore
        /// </summary>
        public StoreOptions Options => _store.Options;

        /// <summary>
        ///     Used to remove document data and tables from the current Postgresql database
        /// </summary>
        public IDocumentCleaner Clean { get; }


        public ISerializer Serializer => _store.Serializer;




    }


}