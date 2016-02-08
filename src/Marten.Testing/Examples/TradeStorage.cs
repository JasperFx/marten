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

namespace Marten.GeneratedCode
{

    public class TradeStorage : IDocumentStorage, IBulkLoader<Trade>, IdAssignment<Trade>, IResolver<Trade>
    {

        private readonly Marten.Schema.Sequences.ISequence _sequence;

        public TradeStorage(Marten.Schema.Sequences.ISequence sequence)
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
            return new NpgsqlCommand("select data from mt_doc_trade where id = :id").With("id", id);
        }


        public NpgsqlCommand DeleteCommandForId(object id)
        {
            return new NpgsqlCommand("delete from mt_doc_trade where id = :id").With("id", id);
        }


        public NpgsqlCommand DeleteCommandForEntity(object entity)
        {
            return DeleteCommandForId(((Trade)entity).Id);
        }


        public NpgsqlCommand LoadByArrayCommand<T>(T[] ids)
        {
            return new NpgsqlCommand("select data, id from mt_doc_trade where id = ANY(:ids)").With("ids", ids);
        }



        public NpgsqlCommand UpsertCommand(Trade document, string json)
        {
            return new NpgsqlCommand("mt_upsert_trade")
                .AsSproc()
                .With("id", document.Id)
                .WithJsonParameter("doc", json).With("arg_value", document.Value);
        }


        public object Assign(Trade document)
        {
            if (document.Id == 0) document.Id = _sequence.NextInt();
            return document.Id;
        }


        public object Retrieve(Trade document)
        {
            return document.Id;
        }


        public NpgsqlDbType IdType => NpgsqlDbType.Integer;

        public object Identity(object document)
        {
            return ((Marten.Testing.Examples.Trade)document).Id;
        }



        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            var document = (Marten.Testing.Examples.Trade)entity;
            batch.Sproc("mt_upsert_trade").Param(document.Id, NpgsqlDbType.Integer).JsonEntity(document).Param(document.Value, NpgsqlDbType.Double);
        }


        public void RegisterUpdate(UpdateBatch batch, object entity, string json)
        {
            var document = (Marten.Testing.Examples.Trade)entity;
            batch.Sproc("mt_upsert_trade").Param(document.Id, NpgsqlDbType.Integer).JsonBody(json).Param(document.Value, NpgsqlDbType.Double);
        }

        public void Remove(IIdentityMap map, object entity)
        {
            throw new NotImplementedException();
        }

        public void Delete(IIdentityMap map, object id)
        {
            throw new NotImplementedException();
        }

        public void Store(IIdentityMap map, object id, object entity)
        {
            throw new NotImplementedException();
        }


        public void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<Trade> documents)
        {
            using (var writer = conn.BeginBinaryImport("COPY mt_doc_trade(id, data, value) FROM STDIN BINARY"))
            {
                foreach (var x in documents)
                {
                    writer.StartRow();
                    writer.Write(x.Id, NpgsqlDbType.Integer);
                    writer.Write(serializer.ToJson(x), NpgsqlDbType.Jsonb);
                    writer.Write(x.Value, NpgsqlDbType.Double);
                }

            }

        }

        public Trade Resolve(DbDataReader reader, IIdentityMap map)
        {
            var json = reader.GetString(0);
            var id = reader[1];
            
            return map.Get<Trade>(id, json);
        }

        public Trade Build(DbDataReader reader, ISerializer serializer)
        {
            throw new NotImplementedException();
        }

        public Trade Resolve(IIdentityMap map, ILoader loader, object id)
        {
            throw new NotImplementedException();
        }

        public Task<Trade> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id)
        {
            throw new NotImplementedException();
        }
    }




}
// ENDSAMPLE