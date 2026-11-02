# Compatibility

BenScr.TextMatching 1.0.0 is a new, independent API with no previous package or legacy API to replace.
It targets .NET 10. Package, repository, assembly and namespace use BenScr.TextMatching without a
language suffix. It has no runtime package dependencies and does not depend on another BenScr package.

Distances count Unicode scalars. Code-unit, grapheme, case-insensitive, normalized, weighted or
Damerau distance implementations have different contracts and are not drop-in equivalents.
Apply any required text normalization explicitly and consistently before comparison. The public
surface is checked against docs/PublicApi.txt. Changes to the defined comparison, ranking or
resource-limit behavior require a compatibility review.
