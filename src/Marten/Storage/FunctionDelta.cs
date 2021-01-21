using System;
using Baseline;
using Marten.Schema;
using Marten.Util;

namespace Marten.Storage
{
    public class FunctionDelta
    {
        public FunctionBody Expected { get; set; }
        public FunctionBody Actual { get; set; }

        public FunctionDelta(FunctionBody expected, FunctionBody actual)
        {
            Expected = expected;
            Actual = actual;
        }

        public bool AllNew => Actual == null;

        public bool Removed => Expected == null && Actual != null;

        public bool HasChanged => AllNew || (Expected != null && !Expected.Body.CanonicizeSql().Equals(Actual.Body.CanonicizeSql(), StringComparison.OrdinalIgnoreCase));

        public void WritePatch(SchemaPatch patch)
        {
            if (AllNew)
            {
                patch.Updates.Apply(this, Expected.Body);

                Expected.DropStatements.Each(drop =>
                {
                    if (!drop.EndsWith("cascade", StringComparison.OrdinalIgnoreCase))
                    {
                        drop = drop.TrimEnd(';') + " cascade;";
                    }

                    patch.Rollbacks.Apply(this, drop);
                });
            }
            else if (HasChanged)
            {
                Actual.DropStatements.Each(drop =>
                {
                    if (!drop.EndsWith("cascade", StringComparison.OrdinalIgnoreCase))
                    {
                        drop = drop.TrimEnd(';') + " cascade;";
                    }

                    patch.Updates.Apply(this, drop);
                });

                patch.Updates.Apply(this, Expected.Body);

                Expected.DropStatements.Each(drop =>
                {
                    patch.Rollbacks.Apply(this, drop);
                });

                patch.Rollbacks.Apply(this, Actual.Body);
            }
        }

        public override string ToString()
        {
            return Expected.Function.QualifiedName + " Diff";
        }
    }
}
