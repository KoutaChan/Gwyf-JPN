using System;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

internal static class DisplaySourceSetMatcher
{
    public static bool Matches(CandidateEntry candidate, DisplaySourceSetMapping sourceSet)
    {
        var context = candidate.Context;
        if (context == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sourceSet.File) &&
            !string.Equals(context.File, sourceSet.File, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (sourceSet.ClassId.HasValue && context.ClassId != sourceSet.ClassId.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sourceSet.ScriptId) &&
            !string.Equals(context.ScriptId, sourceSet.ScriptId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sourceSet.OldTypeHash) &&
            !string.Equals(context.OldTypeHash, sourceSet.OldTypeHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (sourceSet.RawStringIndex.HasValue && context.RawStringIndex != sourceSet.RawStringIndex.Value)
        {
            return false;
        }

        return true;
    }
}
