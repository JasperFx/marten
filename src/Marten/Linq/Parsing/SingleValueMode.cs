namespace Marten.Linq.Parsing;

public enum SingleValueMode
{
    First = 1,
    FirstOrDefault = 2,
    Single = 3,
    SingleOrDefault = 4,
    Count = 5,
    LongCount = 6,
    Any = 7,

    Average = 11,
    Sum = 12,
    Max = 13,
    Min = 14
}
