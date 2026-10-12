using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text.Json;

var path = Path.GetFullPath(args.Single());
var assembly = new AssemblyLoadContext("build-metadata", isCollectible: true).LoadFromAssemblyPath(path);
using var reader = new PEReader(File.OpenRead(path));
var headers = reader.PEHeaders;
var flags = headers.CorHeader?.Flags ?? 0;
Console.WriteLine(JsonSerializer.Serialize(new
{
    AssemblyVersion = assembly.GetName().Version?.ToString(),
    FileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version,
    InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    TargetFramework = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName,
    AnyCPU = headers.CoffHeader.Machine == Machine.I386 && (flags & CorFlags.ILOnly) != 0
        && (flags & (CorFlags.Requires32Bit | CorFlags.Prefers32Bit)) == 0,
}, new JsonSerializerOptions { WriteIndented = true }));
