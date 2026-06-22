using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GwyfJpn.Core;

namespace GwyfJpn.Extractor;

internal sealed class DisplayTemplateCoverageRule
{
    private static readonly Regex Placeholder = new(@"\{\d+(?::[^}]*)?\}", RegexOptions.Compiled);
    private readonly Regex _match;

    private DisplayTemplateCoverageRule(string template, Regex match)
    {
        Template = template;
        _match = match;
    }

    public string Template { get; }

    public static List<DisplayTemplateCoverageRule> From(
        DisplaySinkMapping mapping,
        IReadOnlySet<string> availableTemplates)
    {
        return mapping.Document.DisplayVariantRules
            .Where(rule => string.Equals(rule.Kind, "template", StringComparison.Ordinal))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Match))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Template))
            .Select(rule => Create(rule, availableTemplates))
            .Where(rule => rule != null)
            .Cast<DisplayTemplateCoverageRule>()
            .ToList();
    }

    public bool Covers(string source)
    {
        if (Placeholder.IsMatch(source))
        {
            return false;
        }

        return !string.Equals(source, Template, StringComparison.Ordinal) && _match.IsMatch(source);
    }

    private static DisplayTemplateCoverageRule? Create(
        DisplayVariantRuleMapping rule,
        IReadOnlySet<string> availableTemplates)
    {
        var template = TextNormalizer.NormalizeSource(rule.Template);
        if (string.IsNullOrWhiteSpace(template) || !availableTemplates.Contains(template))
        {
            return null;
        }

        var match = new Regex(
            rule.Match!,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        return new DisplayTemplateCoverageRule(template, match);
    }
}
