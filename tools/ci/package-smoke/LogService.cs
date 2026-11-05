using System.ServiceModel;

// A WCF service contract in a project with LangVersion 7.3 (Smoke.csproj), as C1 CMS has its log service
// (Composite.Core.WebClient.Logging.WCF.ILogService, LangVersion 7). CoreWCF's source generator, which reached every
// application through the webForCore package, wrote C# 11 code ('file' types, a module initializer) for each service
// contract, and the build failed with CS8370 (issue #134). Applications don't get the generator unless they reference
// CoreWCF themselves.
[ServiceContract]
public interface ILogService
{
    [OperationContract]
    string GetLog(int count);
}

public class LogService : ILogService
{
    public string GetLog(int count)
    {
        return count.ToString();
    }
}
