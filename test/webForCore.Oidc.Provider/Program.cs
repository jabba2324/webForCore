using WebForCoreOidc;

// serve <provider URL>: the provider alone, until stopped.
// scenarios <application URL> <provider URL> <out.json>: the scenarios against an application that uses that provider
// (the provider must be serving there), writing the normalised responses for tools/ci/iis_compare.cs.
if (args.Length == 2 && args[0] == "serve")
{
    await using var provider = await OidcTestProvider.StartAsync(args[1]);
    Console.WriteLine($"OpenID Connect test provider at {provider.Issuer}");
    await provider.WaitForShutdownAsync();
    return 0;
}
if (args.Length == 4 && args[0] == "scenarios")
{
    var results = await Scenarios.RunAllAsync(new Uri(args[1]), new Uri(args[2]), Console.Out);
    File.WriteAllText(args[3], Scenarios.ToJson(results));
    Console.WriteLine($"{results.Count} responses written to {args[3]}");
    return 0;
}
Console.Error.WriteLine("Usage: serve <provider URL> | scenarios <application URL> <provider URL> <out.json>");
return 2;
