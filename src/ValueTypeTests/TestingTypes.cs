using System;
using JasperFx.CodeGeneration.Frames;
using Vogen;

namespace ValueTypeTests;

[ValueObject<Guid>]
public partial struct GuidId;

[ValueObject<int>]
public partial struct IntId;

[ValueObject<long>]
public partial struct LongId;

[ValueObject<string>]
public partial struct StringId;

[ValueObject<DateOnly>]
public partial struct DateId;

public record struct NewGuidId(Guid Value);
public record struct NewIntId(int Value);
public record struct NewStringId(string Value);
public record struct NewLongId(long Value);

public record struct NewDateId(DateOnly Value);



