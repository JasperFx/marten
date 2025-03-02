using System;
using Marten.Events.Aggregation;

namespace AspNetCoreWithMarten;

public class Event1
{
    public Guid Id { get;set;}
    public int Number { get;set;}
}

public class Event2
{
    public Guid Id { get;set; }
    public int Number { get;set; }
}
public class Event3
{
    public Guid Id { get;set;}
    public int Number { get;set;}
}

public class Event4
{
    public Guid Id { get;set; }
    public int Number { get;set; }
}

public class View1{
    public Guid Id {get;set;}
    public bool IsEvent1Applied {get; set;}
    public bool IsEvent2Applied {get; set;}
}

public class View2{
    public Guid Id {get;set;}
    public bool IsEvent3Applied {get; set;}
    public bool IsEvent4Applied {get; set;}
}

public class View1Projection : SingleStreamProjection<View1, Guid>
{
    public void Apply(View1 v, Event1 e){
        v.IsEvent1Applied = true;
    }
    public void Apply(View1 v, Event2 e){
        v.IsEvent2Applied = true;
    }
}
public class View2Projection : SingleStreamProjection<View2, Guid>
{

    public void Apply(View2 v, Event3 e){
        v.IsEvent3Applied = true;
    }
    public void Apply(View2 v, Event4 e){
        v.IsEvent4Applied = true;
    }
}
