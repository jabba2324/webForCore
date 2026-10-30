// webForCore: the IsCriticalException helper winforms' ResX classes use (winforms' ClientUtils), written for this
// project. MIT license. Critical exceptions are not caught and turned into data errors.
using System.Threading;

namespace System.Resources
{
	internal static class ExceptionExtensions
	{
		internal static bool IsCriticalException(this Exception ex) =>
			ex is NullReferenceException or StackOverflowException or OutOfMemoryException or ThreadAbortException
				or IndexOutOfRangeException or AccessViolationException;
	}
}

namespace System.Resources
{
	using System.Runtime.CompilerServices;

	// winforms' argument helpers, used by its ResX classes.
	internal static class ArgumentExtensions
	{
		internal static T OrThrowIfNull<T>(this T argument, [CallerArgumentExpression(nameof(argument))] string paramName = null) where T : class
		{
			ArgumentNullException.ThrowIfNull(argument, paramName);
			return argument;
		}

		internal static string OrThrowIfNullOrEmpty(this string argument, [CallerArgumentExpression(nameof(argument))] string paramName = null)
		{
			ArgumentException.ThrowIfNullOrEmpty(argument, paramName);
			return argument;
		}
	}
}
