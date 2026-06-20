namespace GwyfJpn.Core;

/// <summary>
/// Stable source-kind identifiers for extraction candidates.
/// Serialized JSON uses the string values from <see cref="Value"/>.
/// </summary>
public static class CandidateSourceKind
{
    public const string StreamingJsonString = "streaming_json_string";
    public const string AssetTextAssetString = "asset_textasset_string";
    public const string AssetReviewString = "asset_review_string";
    public const string DllDisplayFlowTemplate = "dll_display_flow_template";
    public const string DllReviewLdstr = "dll_review_ldstr";
    public const string DllPromotedLdstr = "dll_promoted_ldstr";
    public const string DerivedDisplayFragment = "derived_display_fragment";
    public const string DerivedTemplateInstantiation = "derived_template_instantiation";
    public const string ConfiguredDisplaySource = "configured_display_source";
    public const string RuntimeDisplaySink = "runtime_display_sink";

    public static bool IsTrusted(string? sourceKind)
    {
        return sourceKind == StreamingJsonString ||
               sourceKind == DllDisplayFlowTemplate ||
               sourceKind == DllPromotedLdstr ||
               sourceKind == DerivedDisplayFragment ||
               sourceKind == DerivedTemplateInstantiation ||
               sourceKind == ConfiguredDisplaySource ||
               sourceKind == RuntimeDisplaySink;
    }

    public static bool IsAssetSource(string? sourceKind)
    {
        return sourceKind == StreamingJsonString ||
               sourceKind == AssetTextAssetString ||
               sourceKind == AssetReviewString;
    }
}
