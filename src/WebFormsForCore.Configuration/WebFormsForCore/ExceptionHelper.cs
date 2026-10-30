// webForCore: the permission objects the Reference Source's System.Net configuration classes demand (System.dll's
// internal ExceptionHelper). MIT license. Code Access Security is not enforced on .NET, so these demands succeed.
using System.Security;
using System.Security.Permissions;

namespace System.Net
{
	internal static class ExceptionHelper
	{
		internal static readonly WebPermission WebPermissionUnrestricted = new WebPermission(PermissionState.Unrestricted);
		internal static readonly SecurityPermission UnmanagedPermission = new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);
		internal static readonly SocketPermission UnrestrictedSocketPermission = new SocketPermission(PermissionState.Unrestricted);
	}
}
