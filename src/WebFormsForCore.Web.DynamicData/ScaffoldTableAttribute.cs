namespace System.ComponentModel.DataAnnotations
{
	// webForCore: .NET Framework's System.ComponentModel.DataAnnotations.ScaffoldTableAttribute, which .NET dropped (it
	// kept ScaffoldColumnAttribute). Dynamic Data reads it from a table's metadata, and applications put it on their model
	// classes (#8). Same name, namespace and members as the Reference Source's.
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
	public class ScaffoldTableAttribute : Attribute
	{
		public ScaffoldTableAttribute(bool scaffold)
		{
			Scaffold = scaffold;
		}

		public bool Scaffold { get; private set; }
	}
}
