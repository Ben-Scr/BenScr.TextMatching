param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$EvidencePath,
    [string]$ExpectedCommit
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$consumerRoot = Join-Path ([IO.Path]::GetTempPath()) ('benscr-textmatching-consumer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $consumerRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'global.json') -Destination $consumerRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'examples/Program.cs') -Destination $consumerRoot
$escapedFeed = [Security.SecurityElement]::Escape($Source)
$escapedCache = [Security.SecurityElement]::Escape((Join-Path $consumerRoot 'packages'))
@"
<configuration>
  <packageSources>
    <clear />
    <add key="library" value="$escapedFeed" />
    <add key="sdk-packs" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="library"><package pattern="BenScr.TextMatching" /></packageSource>
    <packageSource key="sdk-packs">
      <package pattern="Microsoft.NETCore.App.Ref" />
      <package pattern="Microsoft.AspNetCore.App.Ref" />
      <package pattern="Microsoft.WindowsDesktop.App.Ref" />
      <package pattern="Microsoft.NETCore.App.Host.*" />
    </packageSource>
  </packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
  <config><add key="globalPackagesFolder" value="$escapedCache" /></config>
</configuration>
"@ | Set-Content -LiteralPath (Join-Path $consumerRoot 'NuGet.Config') -Encoding utf8
if ($Source.TrimEnd('/') -eq 'https://api.nuget.org/v3/index.json') {
    # NuGet deduplicates identical feed URLs, so the public feed needs a single complete mapping.
    @"
<configuration>
  <packageSources><clear /><add key="public" value="$escapedFeed" /></packageSources>
  <packageSourceMapping><packageSource key="public"><package pattern="*" /></packageSource></packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
  <config><add key="globalPackagesFolder" value="$escapedCache" /></config>
</configuration>
"@ | Set-Content -LiteralPath (Join-Path $consumerRoot 'NuGet.Config') -Encoding utf8
}
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable>
    <RestoreFallbackFolders></RestoreFallbackFolders>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="BenScr.TextMatching" Version="[1.0.0]" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $consumerRoot 'Consumer.csproj') -Encoding utf8
Push-Location $consumerRoot
try {
    & dotnet restore Consumer.csproj --configfile NuGet.Config --packages (Join-Path $consumerRoot 'packages') --no-cache
    if ($LASTEXITCODE -ne 0) { throw 'Package consumer restore failed.' }
    & dotnet run --project Consumer.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Package consumer execution failed.' }
    $packagePath = Join-Path $consumerRoot 'packages/benscr.textmatching/1.0.0/benscr.textmatching.1.0.0.nupkg'
    $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $stream = $archive.GetEntry('BenScr.TextMatching.nuspec').Open()
        $reader = [IO.StreamReader]::new($stream)
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $actualCommit = $nuspec.package.metadata.repository.commit
        if ($ExpectedCommit -and $actualCommit -ne $ExpectedCommit) { throw 'Installed package source commit differs.' }
    } finally { $archive.Dispose() }
    [ordered]@{
        status = 'passed'
        framework = 'net10.0'
        source = $Source
        repository_commit = $actualCommit
        empty_cache = $true
        project_references = $false
        scenarios = @('known scalar distance', 'supplementary Unicode', 'bounded distance', 'explicit normalization', 'stable duplicate ranking', 'work budget', 'invalid UTF-16')
        verified_at_utc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
} finally { Pop-Location }
