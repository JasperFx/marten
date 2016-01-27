using System;
using Marten.Schema.Sequences;

namespace Marten.Schema
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HiLoSequenceAttribute : MartenAttribute
    {
        private readonly HiloDef _def = new HiloDef();

        public int Increment
        {
            set { _def.Increment = value; }
            get { return _def.Increment; }
        }

        public int MaxLo
        {
            set { _def.MaxLo = value; }
            get { return _def.MaxLo; }
        }

        public override void Modify(DocumentMapping mapping)
        {
            mapping.HiLoSettings(_def);
        }
    }
}