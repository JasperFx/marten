using System;
using Marten.Schema.Sequences;

namespace Marten.Schema
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HiloSequenceAttribute : MartenAttribute
    {
        private readonly HiloSettings _settings = new HiloSettings();

        public int Increment
        {
            set { _settings.Increment = value; }
            get { return _settings.Increment; }
        }

        public int MaxLo
        {
            set { _settings.MaxLo = value; }
            get { return _settings.MaxLo; }
        }

        public override void Modify(DocumentMapping mapping)
        {
            mapping.HiloSettings(_settings);
        }
    }
}