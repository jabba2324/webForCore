// webForCore: System.EnterpriseServices.TransactionOption (the <%@ Page Transaction=... %> values), which .NET does not
// have; written from its documented values. MIT license.
#if !NETFRAMEWORK
namespace System.EnterpriseServices
{
	[Serializable]
	public enum TransactionOption
	{
		Disabled = 0,
		NotSupported = 1,
		Supported = 2,
		Required = 3,
		RequiresNew = 4,
	}
}
#endif
