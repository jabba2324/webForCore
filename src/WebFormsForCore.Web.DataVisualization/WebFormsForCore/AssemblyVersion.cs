// webForCore: System.Web.DataVisualization's version, 4.0.0.0 as on .NET Framework. Microsoft's WebForm/AssemblyInfo.cs
// applies ThisAssembly.Version only #if VS_BUILD, which Microsoft's build defined; without it the assembly was 0.0.0.0,
// and libraries built against .NET Framework's System.Web.DataVisualization wouldn't bind (#40).
[assembly: System.Reflection.AssemblyVersion(ThisAssembly.Version)]
[assembly: System.Reflection.AssemblyFileVersion(ThisAssembly.Version)]
