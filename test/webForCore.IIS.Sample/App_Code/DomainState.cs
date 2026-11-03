using System;

public static class DomainState
{
    public static readonly Guid Id = Guid.NewGuid();
    public static int ApplicationStarts;
    // RegressionTests (#33): App_GlobalResources' string as Application_Start read it, or the exception it got.
    public static string GlobalResourceAtStart;
}
