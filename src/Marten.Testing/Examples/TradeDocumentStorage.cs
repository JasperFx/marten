// SAMPLE: generated_trade_storage
using Marten;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Examples;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Remotion.Linq;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema.BulkLoading;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;

namespace Marten.GeneratedCode
{

    public class TradeStorage : Resolver<Trade>, IDocumentStorage, IBulkLoader<Trade>, IdAssignment<Trade>, IResolver<Trade>
    {

        private readonly ISequence _sequence;

        public TradeStorage(IDocumentMapping mapping, ISequence sequence) : base(mapping)
        {
            _sequence = sequence;
        }


        public Type DocumentType => typeof(Trade);

        public NpgsqlCommand UpsertCommand(object document, string json)
        {
            return UpsertCommand((Trade)document, json);
        }


        public NpgsqlCommand LoaderCommand(object id)
        {
            return new NpgsqlCommand("select data, id from public.mt_doc_trade as d where id = :id").With("id", id);
        }


        public NpgsqlCommand DeleteCommandForId(object id)
        {
            return new NpgsqlCommand("delete from public.mt_doc_trade where id = :id").With("id", id);
        }


        public NpgsqlCommand DeleteCommandForEntity(object entity)
        {
            return DeleteCommandForId(((Trade)entity).Id);
        }


        public NpgsqlCommand LoadByArrayCommand<T>(T[] ids)
        {
            return new NpgsqlCommand("select data, id from public.mt_doc_trade as d where id = ANY(:ids)").With("ids", ids);
        }


        public void Remove(IIdentityMap map, object entity)
        {
            var id = Identity(entity);
            map.Remove<Trade>(id);
        }


        public void Delete(IIdentityMap map, object id)
        {
            map.Remove<Trade>(id);
        }


        public void Store(IIdentityMap map, object id, object entity)
        {
            map.Store<Trade>(id, (Trade)entity);
        }


        public object Assign(Trade document, out bool assigned)
        {

            if (document.Id == 0)
            {
                document.Id = _sequence.NextInt();
                assigned = true;
            }

            else
            {
                assigned = false;
            }


            return document.Id;
        }

        public void Assign(Trade document, object id)
        {
            document.Id = (int) id;
        }


        public object Retrieve(Trade document)
        {
            return document.Id;
        }



        public NpgsqlDbType IdType => NpgsqlDbType.Integer;

        public object Identity(object document)
        {
            return ((Trade)document).Id;
        }



        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            var document = (Trade)entity;
            var function = new FunctionName("public", "mt_upsert_trade");
            batch.Sproc(function).Param("arg_value", document.Value, NpgsqlDbType.Double).JsonEntity("doc", document).Param("docId", document.Id, NpgsqlDbType.Integer);
        }


        public void RegisterUpdate(UpdateBatch batch, object entity, string json)
        {
            var document = (Trade)entity;
            var function = new FunctionName("public", "mt_upsert_trade");
            batch.Sproc(function).Param("arg_value", document.Value, NpgsqlDbType.Double).JsonBody("doc", json).Param("docId", document.Id, NpgsqlDbType.Integer);
        }




        public void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<Trade> documents)
        {
            using (var writer = conn.BeginBinaryImport("COPY public.mt_doc_trade(\"value\", \"data\", \"id\") FROM STDIN BINARY"))
            {
                foreach (var x in documents)
                {
                    bool assigned = false;
                    Assign(x, out assigned);
                    writer.StartRow();
                    writer.Write(x.Value, NpgsqlDbType.Double);
                    writer.Write(serializer.ToJson(x), NpgsqlDbType.Jsonb);
                    writer.Write(x.Id, NpgsqlDbType.Integer);
                }

            }

        }




    }




}
// ENDSAMPLE