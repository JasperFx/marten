using System;

namespace Marten.Diagnostics;
public class DiagnosticCategory<T>
{
    private const string OuterClassName = "." + nameof(DiagnosticCategory);
    public static string Name { get; } = ToName(typeof(T));

    public override string ToString() => Name;

    public static implicit operator string(DiagnosticCategory<T> diagnosticCategory) => diagnosticCategory.ToString();
    private static string ToName(Type categoryType)
    {
        var name = categoryType.FullName!.Replace('+', '.');
        var index = name.IndexOf(OuterClassName, StringComparison.Ordinal);
        if (index >= 0)
        {
#if NETSTANDARD2_0
            name = name.Substring(0, index) + name.Substring((index + OuterClassName.Length));
#else
            name = name[..index] + name[(index + OuterClassName.Length)..];
#endif
        }

        return name;
    }
}
