param([string]$ReleaseTag)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($args -join ' ')" }
}
Push-Location $projectRoot
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source commit.' }
    if (& git status --porcelain) { throw 'Verification requires a clean checkout.' }
    if ($ReleaseTag) {
        $tagCommit = & git rev-list -n 1 $ReleaseTag
        if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $commit -or $ReleaseTag -ne 'packages/BenScr.TextMatching/1.0.0') {
            throw 'The release tag does not identify this version and commit.'
        }
    }
    if (-not (Test-Path -LiteralPath docs/PublicApi.txt)) { throw 'Missing reviewed API baseline.' }
    $output = Join-Path $projectRoot ('artifacts/verification-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    foreach ($project in @('src/BenScr.TextMatching.csproj', 'tests/BenScr.TextMatching.Tests.csproj', 'examples/Example.csproj', 'tools/PackageAudit/PackageAudit.csproj')) {
        Invoke-DotNet restore $project --locked-mode
        Invoke-DotNet build $project -c Release --no-restore
        Invoke-DotNet format whitespace $project --no-restore --verify-no-changes
        Invoke-DotNet format style $project --no-restore --verify-no-changes --severity error
    }
    Invoke-DotNet test --project tests/BenScr.TextMatching.Tests.csproj -c Release --no-build --no-restore --report-xunit-trx --report-xunit-trx-filename tests.trx --results-directory $output
    Invoke-DotNet run --project tools/PackageAudit -c Release --no-build -- src/bin/Release/net10.0/BenScr.TextMatching.dll docs/PublicApi.txt
    Invoke-DotNet run --project examples/Example.csproj -c Release --no-build
    Invoke-DotNet pack src/BenScr.TextMatching.csproj -c Release --no-build --no-restore -o $output
    $package = Join-Path $output 'BenScr.TextMatching.1.0.0.nupkg'
    $symbols = Join-Path $output 'BenScr.TextMatching.1.0.0.snupkg'
    Invoke-DotNet run --project tools/PackageAudit -c Release --no-build -- package $package $symbols $commit $projectRoot
    & (Join-Path $PSScriptRoot 'TestPackage.ps1') -Source $output -ExpectedCommit $commit -EvidencePath (Join-Path $output 'consumer-verification.json')
    [xml]$report = Get-Content -LiteralPath (Join-Path $output 'tests.trx') -Raw
    $counters = $report.TestRun.ResultSummary.Counters
    if ([int]$counters.total -le 0 -or [int]$counters.passed -ne [int]$counters.total) { throw 'Incomplete test execution.' }
    $audit = Get-Content -LiteralPath (Join-Path $output 'package-audit.json') -Raw | ConvertFrom-Json
    [ordered]@{
        schema_version = 1
        status = $(if ($ReleaseTag) { 'ready_to_publish' } else { 'tested_local' })
        verified_at_utc = [DateTime]::UtcNow.ToString('o')
        repository = 'https://github.com/Ben-Scr/BenScr.TextMatching'
        commit = $commit
        release_tag = $ReleaseTag
        package_id = $audit.package_id
        version = $audit.version
        package_file = 'BenScr.TextMatching.1.0.0.nupkg'
        package_sha256 = $audit.package_sha256
        symbols_file = 'BenScr.TextMatching.1.0.0.snupkg'
        symbols_sha256 = $audit.symbols_sha256
        tests = @{total=[int]$counters.total; passed=[int]$counters.passed; failed=[int]$counters.failed}
        consumer_frameworks = @('net10.0')
        consumer_verified = $true
        archive_manifest_checked = $true
        published = $false
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'release-manifest.json') -Encoding utf8
    $output | Set-Content -LiteralPath artifacts/latest-verification.txt -Encoding utf8
    Write-Output "Verified artifacts: $output"
} finally { Pop-Location }
