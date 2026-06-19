using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

/// <summary>
/// Enriches static extraction before merge:
/// promotes display-bound DLL ldstr, adds configured runtime labels, and derives interaction UI fragments.
/// </summary>
internal static class DisplayCandidateExpander
{
    public static List<CandidateEntry> EnrichForMerge(
        IEnumerable<CandidateEntry> assets,
        IEnumerable<CandidateEntry> dll,
        DisplaySinkMapping mapping)
    {
        var assetList = assets.ToList();
        var dllList = dll.ToList();
        var baseCandidates = assetList.Concat(dllList);

        var enrichment = LdstrPromoter
            .Expand(dllList, mapping)
            .Concat(SupplementalSources.Expand(mapping));

        var withEnrichment = baseCandidates.Concat(enrichment);
        var fragments = InteractionFragmentDeriver.Expand(withEnrichment);
        var withFragments = withEnrichment.Concat(fragments).ToList();

        return withFragments.Concat(DisplayVariantExpander.Expand(withFragments, mapping)).ToList();
    }

    private static class CandidateFactories
    {
        public static CandidateEntry PromotedLdstr(CandidateEntry reviewEntry)
        {
            var promotedId = reviewEntry.Id.StartsWith("dll-text:", StringComparison.Ordinal)
                ? "dll-promoted:" + reviewEntry.Id["dll-text:".Length..]
                : "dll-promoted:" + reviewEntry.Id;

            return WithKind(reviewEntry, promotedId, CandidateSourceKind.DllPromotedLdstr);
        }

        public static CandidateEntry ConfiguredSource(string source)
        {
            return new CandidateEntry
            {
                Id = $"configured-source:{StableId.Hash(source)}",
                Source = source,
                SourceKind = CandidateSourceKind.ConfiguredDisplaySource,
                Context = new CandidateContext
                {
                    Type = "SettingsLayoutRuntimeUI",
                    Method = "CreateRebindEntry"
                }
            };
        }

        public static CandidateEntry DerivedFragment(CandidateEntry parent, string fragment)
        {
            return new CandidateEntry
            {
                Id = $"derived-fragment:{StableId.Hash(parent.Id)}:{StableId.Hash(fragment)}",
                Source = fragment,
                SourceKind = CandidateSourceKind.DerivedDisplayFragment,
                Context = parent.Context
            };
        }

        private static CandidateEntry WithKind(CandidateEntry entry, string id, string sourceKind)
        {
            return new CandidateEntry
            {
                Id = id,
                Source = entry.Source,
                SourceKind = sourceKind,
                Context = entry.Context
            };
        }
    }

    private static class LdstrPromoter
    {
        public static IEnumerable<CandidateEntry> Expand(
            IEnumerable<CandidateEntry> dllEntries,
            DisplaySinkMapping mapping)
        {
            var rules = new PromotionRules(mapping);
            foreach (var entry in dllEntries)
            {
                if (entry.SourceKind != CandidateSourceKind.DllReviewLdstr || !rules.ShouldPromote(entry))
                {
                    continue;
                }

                yield return CandidateFactories.PromotedLdstr(entry);
            }
        }

        private readonly struct PromotionRules
        {
            private readonly DisplaySinkMapping _mapping;
            private readonly HashSet<string> _knownFieldTypes;
            private readonly HashSet<string> _auxiliaryTypes;

            public PromotionRules(DisplaySinkMapping mapping)
            {
                _mapping = mapping;
                _knownFieldTypes = ToTypeSet(mapping.Document.KnownDisplayFields.Select(field => field.TypeName));
                _auxiliaryTypes = ToTypeSet(mapping.Document.PromotedDisplayTypes);
            }

            public bool ShouldPromote(CandidateEntry entry)
            {
                var source = entry.Source ?? string.Empty;
                if (!IsPromotableText(source))
                {
                    return false;
                }

                var typeName = entry.Context?.Type ?? string.Empty;
                var methodName = entry.Context?.Method ?? string.Empty;
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return false;
                }

                if (_mapping.FindGameSink(typeName, methodName) != null)
                {
                    return true;
                }

                if (_knownFieldTypes.Contains(typeName))
                {
                    return true;
                }

                return _auxiliaryTypes.Contains(typeName) && DisplayMethodHeuristics.IsLikelyDisplayMethod(methodName);
            }

            private static HashSet<string> ToTypeSet(IEnumerable<string> typeNames)
            {
                return typeNames
                    .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
                    .ToHashSet(StringComparer.Ordinal);
            }

            private static bool IsPromotableText(string source)
            {
                return SourceTextClassifier.IsMechanicallyReadableText(source) &&
                       TextNormalizer.LooksTranslatableEnglish(source) &&
                       !IdentifierHeuristics.LooksLikeInternalKey(source);
            }
        }
    }

    private static class SupplementalSources
    {
        public static IEnumerable<CandidateEntry> Expand(DisplaySinkMapping mapping)
        {
            foreach (var source in mapping.Document.SupplementalDisplaySources)
            {
                var normalized = SourceTextClassifier.NormalizeConfiguredDisplay(source);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                yield return CandidateFactories.ConfiguredSource(normalized);
            }
        }
    }

    private static class InteractionFragmentDeriver
    {
        private static readonly Regex BracketKeyAction = new(
            @"^\[(?:[^\]]+)\]\s+(.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex HoldPressToAction = new(
            @"^(?:Hold|Press)\s+\[(?:[^\]]+)\]\s+to\s+(.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex PressKeyOnly = new(
            @"^Press\s+\[(?:[^\]]+)\]\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static IEnumerable<CandidateEntry> Expand(IEnumerable<CandidateEntry> entries)
        {
            var seenSources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                foreach (var fragment in DeriveFragments(entry.Source))
                {
                    var normalized = SourceTextClassifier.NormalizeCandidate(fragment);
                    if (!IsUsableFragment(normalized) || !seenSources.Add(normalized))
                    {
                        continue;
                    }

                    yield return CandidateFactories.DerivedFragment(entry, normalized);
                }
            }
        }

        private static bool IsUsableFragment(string normalized)
        {
            return !string.IsNullOrWhiteSpace(normalized) &&
                   SourceTextClassifier.IsMechanicallyReadableText(normalized);
        }

        private static IEnumerable<string> DeriveFragments(string? source)
        {
            var normalized = SourceTextClassifier.NormalizeCandidate(source);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                yield break;
            }

            foreach (var fragment in FromBracketKeyAction(normalized))
            {
                yield return fragment;
            }

            foreach (var fragment in FromHoldPressToAction(normalized))
            {
                yield return fragment;
            }

            if (PressKeyOnly.IsMatch(normalized))
            {
                yield return "Press ";
            }
        }

        private static IEnumerable<string> FromBracketKeyAction(string normalized)
        {
            var match = BracketKeyAction.Match(normalized);
            if (!match.Success)
            {
                yield break;
            }

            var action = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(action))
            {
                yield return action;
            }
        }

        private static IEnumerable<string> FromHoldPressToAction(string normalized)
        {
            var match = HoldPressToAction.Match(normalized);
            if (!match.Success)
            {
                yield break;
            }

            var action = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                yield break;
            }

            yield return "to " + action;
            yield return action;
        }
    }

    private static class DisplayMethodHeuristics
    {
        private static readonly string[] DisplayMethodPrefixes =
        {
            "Show", "Open", "Create", "Populate", "Update", "Handle"
        };

        public static bool IsLikelyDisplayMethod(string methodName)
        {
            if (string.IsNullOrWhiteSpace(methodName))
            {
                return false;
            }

            if (methodName is ".ctor" or "Awake" or "Start" or "OnEnable")
            {
                return true;
            }

            if (IsSetDisplayMethod(methodName))
            {
                return true;
            }

            return DisplayMethodPrefixes.Any(prefix =>
                methodName.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static bool IsSetDisplayMethod(string methodName)
        {
            // Match SetText/SetLabel but not SetupLobbyData.
            return methodName.Length > 3 &&
                   methodName.StartsWith("Set", StringComparison.Ordinal) &&
                   char.IsUpper(methodName[3]);
        }
    }

    private static class IdentifierHeuristics
    {
        /// <summary>
        /// Filters camelCase field keys such as LobbyCode/name while keeping short ALL-CAPS UI labels.
        /// </summary>
        public static bool LooksLikeInternalKey(string source)
        {
            if (source.Any(char.IsWhiteSpace))
            {
                return false;
            }

            if (source.Length <= 8 && source.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)))
            {
                return false;
            }

            if (char.IsLower(source[0]))
            {
                return true;
            }

            for (var index = 1; index < source.Length; index++)
            {
                if (char.IsUpper(source[index]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
