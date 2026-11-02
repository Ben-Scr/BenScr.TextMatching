# Provenance

Created by Ben Schneider — https://github.com/Ben-Scr

The scalar decoding, bounded edit distance and fuzzy ranking implementations in this repository are
new independent code. No private application or third-party algorithm source was copied. The public
GitHub target name was checked before repository creation. Package metadata and Source Link identify
the exact source commit rather than an unversioned branch.

Build and package-audit infrastructure adapts BenScr.Resilience under the same author license.
Its original custom LICENSE.txt is retained byte-for-byte, including CRLF and UTF-8 encoding.
SHA-256: `f6600e285e17c48188a65745d7595807e1755c72201e8eef201a9174fca18573`.

The unchanged shared icon is stored at branding/BenScr_Icon_Master.png and embedded as icon.png.
It is a 1254 × 1254 transparent PNG of 795819 bytes, SHA-256
`a5dcabceb766e13c1840a95130ee98a458e12e3713fd9b07f3ceae75a1f5b993`.
Its identical bytes reuse the shared 32/128-pixel light/dark visual review; final gallery rendering
is separately checked during release. No resized or modified logo is represented as the original.

Text contracts use the platform's scalar decoder and distinguish scalar values from displayed
graphemes, as described in the primary platform documentation:

- [Rune.DecodeFromUtf16](https://learn.microsoft.com/en-us/dotnet/api/system.text.rune.decodefromutf16?view=net-10.0)
- [Character encoding in .NET](https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-encoding-introduction)

Test/build dependencies retain their respective licenses and are not shipped as runtime dependencies.
