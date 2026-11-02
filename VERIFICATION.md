# Verification

Run `pwsh -File tools/Verify.ps1` from a clean committed checkout with .NET SDK 10.0.303 and PowerShell 7.
The pinned xUnit v3 4.0.2 runner uses Microsoft.Testing.Platform. No tests depend on network services.
CI runs the same script on Windows and Ubuntu; its executed status is recorded by each workflow run,
not inferred from the existence of the workflow file.

The suite covers known edit distances, supplementary characters, combining sequences, strict malformed
UTF-16 rejection, inclusive thresholds, large thresholds, bounds, recurrence budgets, empty data,
culture independence, concurrent calls, stable ranking, duplicate candidates, read-only ownership,
single enumeration, disposal and iterator failures. Malformed strings are constructed at runtime to
avoid attribute metadata encoding replacing invalid surrogates during test discovery.

A full matrix reference checks 2,500 fixed-seed Unicode input pairs and all thresholds 0 through 18
in both directions. An independent full-sort reference checks 500 fixed-seed fuzzy searches. The
production implementation uses rolling rows, a diagonal band and a bounded priority queue instead.
These are deterministic correctness checks, not performance benchmarks or linguistic quality claims.

The verification script also performs locked restores, release builds with warnings as errors,
whitespace/style verification, API-baseline comparison, example execution, packing and archive audits.
The archive check requires exactly the expected entries, original license/icon hashes, no runtime
dependencies, matching Portable PDBs, and source checksums tied to the commit. A new consumer outside
the source tree restores from an isolated local feed into an empty package cache and executes the
example. Artifacts contain tests.trx, package-audit.json, consumer-verification.json and
release-manifest.json. The latter records concrete results and exact archive hashes.

After publication, use the same consumer script with the public feed and release commit. Publication,
public symbols, signed archive payloads and gallery rendering are separate release checks. A local
successful build alone does not establish that a package is published or indexed.
