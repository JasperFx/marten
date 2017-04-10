using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Services
{
    public interface IBatchCommand
    {
        NpgsqlParameter AddParameter(object value, NpgsqlDbType dbType);
        ISerializer Serializer { get; }
        NpgsqlCommand Command { get; }
    }

    public class BatchCommand : IBatchCommand
    {
        public ISerializer Serializer { get; }
        private int _counter = 0;

        public BatchCommand(ISerializer serializer)
        {
            Serializer = serializer;
        }

        public NpgsqlCommand Command { get; } = new NpgsqlCommand();

        public int Count => Calls.Count;

        public NpgsqlParameter AddParameter(object value, NpgsqlDbType dbType)
        {
            var name = "p" + _counter++;
            var param = Command.AddNamedParameter(name, value);
            param.NpgsqlDbType = dbType;

            return param;
        }

        public IList<ICallback> Callbacks { get; } = new List<ICallback>();

        public IList<IStorageOperation> Calls { get; } = new List<IStorageOperation>();

        public NpgsqlCommand BuildCommand()
        {
            return CommandBuilder.ToBatchCommand(Calls);
        }

        public void AddCall(IStorageOperation call, ICallback callback = null)
        {
            Calls.Add(call);
            Callbacks.Add(callback);
        }

        public SprocCall Sproc(DbObjectName function, ICallback callback = null)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));

            var call = new SprocCall(this, function);
            AddCall(call, callback);

            return call;
        }

        public bool HasCallbacks()
        {
            return Callbacks.Any(x => x != null);
        }

    }
}