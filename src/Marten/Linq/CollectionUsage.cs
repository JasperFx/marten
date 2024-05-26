#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Linq.Parsing;

namespace Marten.Linq;

public partial class CollectionUsage
{
    private readonly StoreOptions _options;


    private int? _limit;

    private int? _offset;

    public CollectionUsage(StoreOptions options, Type elementType)
    {
        _options = options;
        ElementType = elementType;
    }

    public Type ElementType { get; }

    public SingleValueMode? SingleValueMode { get; set; }
    public bool IsAny { get; set; }
    public bool IsDistinct { get; set; }
    public CollectionUsage Inner { get; internal set; } = null!;
    public Expression SelectMany { get; set; } = null!;


    public void WriteLimit(int limit)
    {
        _limit ??= limit; // don't overwrite
    }

    public void WriteOffset(int offset)
    {
        _offset ??= offset; // don't overwrite
    }
}
