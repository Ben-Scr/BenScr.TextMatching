using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length == 2)
{
    Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
    string api = string.Join("\n", assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal).SelectMany(type =>
        new[] { $"{type.FullName} : {type.BaseType}" }.Concat(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(member => "  " + member.MemberType + " " + member).Order(StringComparer.Ordinal)))) + "\n";
    if (File.Exists(args[1]))
    {
        Check(File.ReadAllText(args[1]).Replace("\r\n", "\n") == api, "Public API differs: " + args[1]);
        Console.WriteLine("Public API baseline matches.");
    }
    else
    {
        File.WriteAllText(args[1], api);
        Console.WriteLine("API inventory written to " + args[1]);
    }
    return;
}

if (args.Length != 5 || args[0] != "package")
{
    throw new ArgumentException("Usage: PackageAudit <assembly> <API file> OR package <nupkg> <snupkg> <commit> <root>");
}
string packagePath = args[1];
string symbolsPath = args[2];
string commit = args[3];
string root = Path.GetFullPath(args[4]);
using var package = ZipFile.OpenRead(packagePath);
using var symbols = ZipFile.OpenRead(symbolsPath);
CheckManifest(package, new[]
{
    "_rels/.rels", "BenScr.TextMatching.nuspec", "LICENSE.txt", "README.md", "icon.png",
    "lib/net10.0/BenScr.TextMatching.dll", "lib/net10.0/BenScr.TextMatching.xml", "[Content_Types].xml"
});
CheckManifest(symbols, new[]
{
    "_rels/.rels", "BenScr.TextMatching.nuspec", "lib/net10.0/BenScr.TextMatching.pdb", "[Content_Types].xml"
});
XDocument specification = XDocument.Load(new MemoryStream(Read(package, "BenScr.TextMatching.nuspec")));
XElement metadata = specification.Root!.Elements().Single();
string Value(string name) => metadata.Elements().Single(element => element.Name.LocalName == name).Value;
Check(Value("id") == "BenScr.TextMatching" && Value("version") == "1.0.0", "Package identity mismatch.");
XElement repository = metadata.Elements().Single(element => element.Name.LocalName == "repository");
Check((string?)repository.Attribute("commit") == commit, "Repository commit mismatch.");
Check((string?)repository.Attribute("url") == "https://github.com/Ben-Scr/BenScr.TextMatching", "Repository URL mismatch.");
Check(!metadata.Descendants().Any(element => element.Name.LocalName == "dependency"), "Unexpected runtime dependencies.");
Check(Value("icon") == "icon.png" && Value("readme") == "README.md" && Value("license") == "LICENSE.txt", "Package metadata paths mismatch.");
Check((string?)metadata.Elements().Single(element => element.Name.LocalName == "license").Attribute("type") == "file", "License must use the original file.");
Check(Convert.ToHexStringLower(SHA256.HashData(Read(package, "LICENSE.txt"))) == "f6600e285e17c48188a65745d7595807e1755c72201e8eef201a9174fca18573", "Original license bytes changed.");
XElement symbolMetadata = XDocument.Load(new MemoryStream(Read(symbols, "BenScr.TextMatching.nuspec"))).Root!.Elements().Single();
Check(symbolMetadata.Elements().Single(element => element.Name.LocalName == "id").Value == Value("id") &&
    symbolMetadata.Elements().Single(element => element.Name.LocalName == "version").Value == Value("version"), "Symbol package identity mismatch.");
Check(XNode.DeepEquals(repository, symbolMetadata.Elements().Single(element => element.Name.LocalName == "repository")), "Symbol package repository mismatch.");
foreach (string name in new[] { "README.md", "LICENSE.txt" })
{
    Check(Read(package, name).SequenceEqual(File.ReadAllBytes(Path.Combine(root, name))), "Packaged document mismatch: " + name);
}
byte[] icon = Read(package, "icon.png");
Check(icon.Length < 1_000_000 && icon.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Invalid PNG.");
Check(Convert.ToHexStringLower(SHA256.HashData(icon)) == "a5dcabceb766e13c1840a95130ee98a458e12e3713fd9b07f3ceae75a1f5b993", "Icon hash mismatch.");
Check(icon.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "branding", "BenScr_Icon_Master.png"))), "Icon differs from master.");
File.WriteAllBytes(Path.Combine(root, "artifacts", "icon-from-package.png"), icon);

var sourceUrls = new HashSet<string>(StringComparer.Ordinal);
foreach (string framework in new[] { "net10.0" })
{
    string stem = $"lib/{framework}/BenScr.TextMatching";
    byte[] assemblyBytes = Read(package, stem + ".dll");
    byte[] pdbBytes = Read(symbols, stem + ".pdb");
    Check(Read(package, stem + ".xml").Length > 1000, "Missing XML documentation.");
    using var pdbStream = new MemoryStream(pdbBytes);
    using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
    MetadataReader reader = provider.GetMetadataReader();
    var pdbId = new BlobContentId(reader.DebugMetadataHeader!.Id);
    using var pe = new PEReader(new MemoryStream(assemblyBytes));
    DebugDirectoryEntry codeView = pe.ReadDebugDirectory().Single(entry => entry.Type == DebugDirectoryEntryType.CodeView);
    CodeViewDebugDirectoryData codeViewData = pe.ReadCodeViewDebugDirectoryData(codeView);
    Check(codeViewData.Guid == pdbId.Guid && codeView.Stamp == pdbId.Stamp, "PDB does not match assembly.");
    var sourceLinkKind = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    CustomDebugInformation sourceLink = reader.CustomDebugInformation
        .Select(reader.GetCustomDebugInformation).Single(info => reader.GetGuid(info.Kind) == sourceLinkKind);
    using JsonDocument json = JsonDocument.Parse(reader.GetBlobBytes(sourceLink.Value));
    JsonProperty mapping = json.RootElement.GetProperty("documents").EnumerateObject().Single();
    string urlPattern = mapping.Value.GetString()!;
    Check(urlPattern == $"https://raw.githubusercontent.com/Ben-Scr/BenScr.TextMatching/{commit}/*", "Source Link URL mismatch.");
    string documentPrefix = mapping.Name.TrimEnd('*');
    foreach (DocumentHandle handle in reader.Documents)
    {
        Document document = reader.GetDocument(handle);
        string name = reader.GetString(document.Name);
        Check(!name.Contains("C:\\Users\\", StringComparison.OrdinalIgnoreCase), "Developer path leaked.");
        if (!name.StartsWith(documentPrefix, StringComparison.Ordinal))
        {
            continue;
        }
        string relative = name[documentPrefix.Length..];
        if (!relative.StartsWith("src/", StringComparison.Ordinal))
        {
            continue;
        }
        byte[] sourceBytes = File.ReadAllBytes(Path.Combine(root, relative));
        Check(reader.GetBlobBytes(document.Hash).SequenceEqual(SHA256.HashData(sourceBytes)), "Source checksum mismatch: " + relative);
        if (relative.Contains("/obj/", StringComparison.Ordinal))
        {
            var embeddedSourceKind = new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
            Check(reader.GetCustomDebugInformation(handle).Select(reader.GetCustomDebugInformation)
                .Any(info => reader.GetGuid(info.Kind) == embeddedSourceKind), "Generated source is not embedded: " + relative);
            continue;
        }
        sourceUrls.Add(urlPattern.Replace("*", relative, StringComparison.Ordinal));
    }
}
Check(sourceUrls.SetEquals(new[]
{
    $"https://raw.githubusercontent.com/Ben-Scr/BenScr.TextMatching/{commit}/src/TextMatch.cs",
    $"https://raw.githubusercontent.com/Ben-Scr/BenScr.TextMatching/{commit}/src/TextMatcher.cs"
}), "Source mapping inventory mismatch.");
string auditPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(packagePath))!, "package-audit.json");
File.WriteAllText(auditPath, JsonSerializer.Serialize(new
{
    package_id = "BenScr.TextMatching",
    version = "1.0.0",
    repository_commit = commit,
    frameworks = new[] { "net10.0" },
    dependencies = Array.Empty<string>(),
    icon_sha256 = Convert.ToHexStringLower(SHA256.HashData(icon)),
    license_sha256 = Convert.ToHexStringLower(SHA256.HashData(Read(package, "LICENSE.txt"))),
    package_sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(packagePath))),
    symbols_sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(symbolsPath))),
    source_urls = sourceUrls.Order(StringComparer.Ordinal).ToArray(),
    nuspec_checked = true,
    archive_inventory_checked = true,
    pdb_assembly_identity_checked = true,
    source_checksums_checked = true
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Package, documentation, license, icon, PDB identities and Source Link checks passed.");

static byte[] Read(ZipArchive archive, string path)
{
    using Stream stream = (archive.GetEntry(path) ?? throw new InvalidDataException("Missing entry: " + path)).Open();
    using var output = new MemoryStream();
    stream.CopyTo(output);
    return output.ToArray();
}

static void CheckManifest(ZipArchive archive, string[] expected)
{
    string[] names = archive.Entries.Select(entry => entry.FullName).ToArray();
    Check(names.Distinct(StringComparer.Ordinal).Count() == names.Length, "Archive contains duplicate entries.");
    string[] properties = names.Where(name => name.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal) &&
        name.EndsWith(".psmdcp", StringComparison.Ordinal)).ToArray();
    Check(properties.Length == 1, "Invalid package core-property inventory.");
    Check(new HashSet<string>(names, StringComparer.Ordinal).SetEquals(expected.Concat(properties)), "Unexpected archive contents.");
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidDataException(message);
    }
}
