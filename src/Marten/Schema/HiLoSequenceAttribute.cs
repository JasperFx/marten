using System;
using Marten.Schema.Identity.Sequences;

namespace Marten.Schema
{
    /// <summary>
    /// Use to customize the Hilo sequence generation for a single document type
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class HiloSequenceAttribute : MartenAttribute
    {
        private readonly HiloSettings _settings = new HiloSettings();


        public int MaxLo
        {
            set { _settings.MaxLo = value; }
            get { return _settings.MaxLo; }
        }

        public override void Modify(DocumentMapping mapping)
        {
            mapping.HiloSettings = _settings;
        }
    }
}