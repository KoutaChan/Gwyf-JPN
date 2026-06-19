using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using dnlib.DotNet;

namespace GwyfJpn.Extractor;

/// <summary>
/// A finite set of possible display strings reconstructed from IL.
/// Literals are fixed English text; placeholders are values that are visible at runtime
/// but should not be translated directly, such as numbers, player names, or API data.
/// </summary>
internal sealed class DisplayTextValue
{
    private const int MaxAlternatives = 64;

    public static readonly DisplayTextValue Unknown = new(new[] { DisplayTextSequence.Placeholder() }, Enumerable.Empty<FieldDef>(), null);
    public static readonly DisplayTextValue Empty = new(new[] { DisplayTextSequence.Empty }, Enumerable.Empty<FieldDef>(), null);

    private DisplayTextValue(IEnumerable<DisplayTextSequence> alternatives, IEnumerable<FieldDef> fields, int? containerId)
    {
        Alternatives = alternatives
            .Distinct()
            .Take(MaxAlternatives)
            .ToList();
        Fields = fields.Distinct().ToList();
        ContainerId = containerId;
    }

    public IReadOnlyList<DisplayTextSequence> Alternatives { get; }
    public IReadOnlyList<FieldDef> Fields { get; }
    public int? ContainerId { get; }
    public bool HasDisplayEvidence => Alternatives.Any(a => a.HasLiteralText) || Fields.Count > 0;
    public bool IsContainerReference => ContainerId.HasValue;

    public static DisplayTextValue FromLiteral(string literal)
    {
        return string.IsNullOrEmpty(literal)
            ? Empty
            : new DisplayTextValue(new[] { DisplayTextSequence.Literal(literal) }, Enumerable.Empty<FieldDef>(), null);
    }

    public static DisplayTextValue FromField(FieldDef field)
    {
        return new DisplayTextValue(new[] { DisplayTextSequence.Placeholder() }, new[] { field }, null);
    }

    public static DisplayTextValue FromContainer(int containerId)
    {
        return new DisplayTextValue(new[] { DisplayTextSequence.Placeholder() }, Enumerable.Empty<FieldDef>(), null);
    }

    public static DisplayTextValue FromEnumType(TypeDef enumType)
    {
        if (!enumType.IsEnum)
        {
            return Unknown;
        }

        return new DisplayTextValue(
            enumType.Fields.Where(f => f.IsLiteral).Select(f => DisplayTextSequence.Literal(IlOpcodeHelpers.FieldName(f))),
            Enumerable.Empty<FieldDef>(),
            null);
    }

    public static DisplayTextValue Choice(IEnumerable<DisplayTextValue> values)
    {
        var list = values.ToList();
        return new DisplayTextValue(
            list.SelectMany(v => v.Alternatives),
            list.SelectMany(v => v.Fields),
            SharedContainerId(list));
    }

    public static DisplayTextValue Concat(IEnumerable<DisplayTextValue> values)
    {
        var parts = values.ToList();
        if (parts.Count == 0)
        {
            return Empty;
        }

        var alternatives = new List<DisplayTextSequence> { DisplayTextSequence.Empty };
        foreach (var value in parts)
        {
            var next = new List<DisplayTextSequence>();
            var rightAlternatives = value.Alternatives.Count == 0
                ? new[] { DisplayTextSequence.Placeholder() }
                : value.Alternatives;
            foreach (var left in alternatives)
            {
                foreach (var right in rightAlternatives)
                {
                    next.Add(left.Append(right));
                    if (next.Count >= MaxAlternatives)
                    {
                        break;
                    }
                }

                if (next.Count >= MaxAlternatives)
                {
                    break;
                }
            }

            alternatives = next;
        }

        return new DisplayTextValue(alternatives, parts.SelectMany(v => v.Fields), null);
    }

    public static DisplayTextValue Format(IMethod method, IReadOnlyList<DisplayTextValue> args)
    {
        if (args.Count == 0)
        {
            return Unknown;
        }

        var format = args[0];
        if (!format.Alternatives.Any(a => a.HasLiteralText))
        {
            return Unknown.WithFields(args.SelectMany(v => v.Fields));
        }

        var argumentAlternatives = BuildFormatArgumentAlternatives(args);
        var templates = new List<DisplayTextSequence>();
        foreach (var formatAlternative in format.Alternatives.Where(a => a.HasLiteralText))
        {
            foreach (var parsed in DisplayTextSequence.FromFormatString(formatAlternative.Render()))
            {
                foreach (var substituted in SubstituteFormatArguments(parsed, argumentAlternatives))
                {
                    templates.Add(substituted);
                    if (templates.Count >= MaxAlternatives)
                    {
                        break;
                    }
                }

                if (templates.Count >= MaxAlternatives)
                {
                    break;
                }
            }

            if (templates.Count >= MaxAlternatives)
            {
                break;
            }
        }

        return templates.Count == 0
            ? Unknown.WithFields(args.SelectMany(v => v.Fields))
            : new DisplayTextValue(templates, args.SelectMany(v => v.Fields), null);
    }

    private static IReadOnlyList<IReadOnlyList<DisplayTextSequence>> BuildFormatArgumentAlternatives(
        IReadOnlyList<DisplayTextValue> args)
    {
        var argumentAlternatives = new List<IReadOnlyList<DisplayTextSequence>>();
        for (var argIndex = 1; argIndex < args.Count; argIndex++)
        {
            var arg = args[argIndex];
            if (arg.Alternatives.Count == 0)
            {
                argumentAlternatives.Add(new[] { DisplayTextSequence.Placeholder() });
                continue;
            }

            argumentAlternatives.Add(arg.Alternatives);
        }

        return argumentAlternatives;
    }

    private static IEnumerable<DisplayTextSequence> SubstituteFormatArguments(
        DisplayTextSequence format,
        IReadOnlyList<IReadOnlyList<DisplayTextSequence>> argumentAlternatives)
    {
        if (argumentAlternatives.Count == 0)
        {
            yield return format;
            yield break;
        }

        var current = new List<DisplayTextSequence> { format };
        for (var argIndex = 0; argIndex < argumentAlternatives.Count; argIndex++)
        {
            var next = new List<DisplayTextSequence>();
            foreach (var sequence in current)
            {
                foreach (var replacement in argumentAlternatives[argIndex])
                {
                    next.Add(sequence.SubstitutePlaceholder(argIndex, replacement));
                    if (next.Count >= MaxAlternatives)
                    {
                        break;
                    }
                }

                if (next.Count >= MaxAlternatives)
                {
                    break;
                }
            }

            current = next;
        }

        foreach (var sequence in current)
        {
            yield return sequence;
        }
    }

    public DisplayTextValue WithFields(IEnumerable<FieldDef> fields)
    {
        return new DisplayTextValue(Alternatives, Fields.Concat(fields), ContainerId);
    }

    public IReadOnlyList<string> RenderTemplates()
    {
        return Alternatives
            .Select(a => a.Render())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<string> LiteralParts()
    {
        return Alternatives
            .SelectMany(a => a.Parts)
            .Where(p => p.Kind == DisplayTextPartKind.Literal)
            .Select(p => p.Text)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static int? SharedContainerId(IReadOnlyList<DisplayTextValue> values)
    {
        int? shared = null;
        var sawContainer = false;
        foreach (var value in values)
        {
            if (!value.ContainerId.HasValue)
            {
                continue;
            }

            if (!sawContainer)
            {
                shared = value.ContainerId.Value;
                sawContainer = true;
                continue;
            }

            if (shared != value.ContainerId.Value)
            {
                return null;
            }
        }

        return shared;
    }
}

internal sealed class DisplayTextSequence : IEquatable<DisplayTextSequence>
{
    private static readonly Regex FormatPlaceholder = new(@"\{(?<index>\d+)(?::[^}]*)?\}", RegexOptions.Compiled);

    public static readonly DisplayTextSequence Empty = new(Array.Empty<DisplayTextPart>());

    private DisplayTextSequence(IEnumerable<DisplayTextPart> parts)
    {
        Parts = Coalesce(parts).ToList();
    }

    public IReadOnlyList<DisplayTextPart> Parts { get; }
    public bool HasLiteralText => Parts.Any(p => p.Kind == DisplayTextPartKind.Literal && p.Text.Any(char.IsLetterOrDigit));

    public static DisplayTextSequence Literal(string text)
    {
        return new DisplayTextSequence(new[] { DisplayTextPart.Literal(text) });
    }

    public static DisplayTextSequence Placeholder()
    {
        return new DisplayTextSequence(new[] { DisplayTextPart.Placeholder() });
    }

    public static IEnumerable<DisplayTextSequence> FromFormatString(string format)
    {
        yield return new DisplayTextSequence(ParseFormatString(format));
    }

    public DisplayTextSequence Append(DisplayTextSequence other)
    {
        return new DisplayTextSequence(Parts.Concat(other.Parts));
    }

    public DisplayTextSequence SubstitutePlaceholder(int placeholderIndex, DisplayTextSequence replacement)
    {
        var output = new List<DisplayTextPart>();
        var seenPlaceholder = 0;
        foreach (var part in Parts)
        {
            if (part.Kind == DisplayTextPartKind.Placeholder)
            {
                if (seenPlaceholder == placeholderIndex)
                {
                    foreach (var replacementPart in replacement.Parts)
                    {
                        output.Add(replacementPart);
                    }
                }
                else
                {
                    output.Add(part);
                }

                seenPlaceholder++;
            }
            else
            {
                output.Add(part);
            }
        }

        return new DisplayTextSequence(output);
    }

    public string Render()
    {
        var placeholderIndex = 0;
        var rendered = new List<string>(Parts.Count);
        foreach (var part in Parts)
        {
            if (part.Kind == DisplayTextPartKind.Literal)
            {
                rendered.Add(part.Text);
            }
            else
            {
                rendered.Add("{" + placeholderIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                placeholderIndex++;
            }
        }

        return string.Concat(rendered);
    }

    public bool Equals(DisplayTextSequence? other)
    {
        if (other == null || Parts.Count != other.Parts.Count)
        {
            return false;
        }

        for (var i = 0; i < Parts.Count; i++)
        {
            if (!Parts[i].Equals(other.Parts[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is DisplayTextSequence other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var part in Parts)
            {
                hash = hash * 31 + part.GetHashCode();
            }

            return hash;
        }
    }

    private static IEnumerable<DisplayTextPart> ParseFormatString(string format)
    {
        var cursor = 0;
        foreach (Match match in FormatPlaceholder.Matches(format))
        {
            if (match.Index > cursor)
            {
                yield return DisplayTextPart.Literal(format.Substring(cursor, match.Index - cursor));
            }

            yield return DisplayTextPart.Placeholder();
            cursor = match.Index + match.Length;
        }

        if (cursor < format.Length)
        {
            yield return DisplayTextPart.Literal(format.Substring(cursor));
        }
    }

    private static IEnumerable<DisplayTextPart> Coalesce(IEnumerable<DisplayTextPart> parts)
    {
        DisplayTextPart? pendingLiteral = null;
        foreach (var part in parts)
        {
            if (part.Kind == DisplayTextPartKind.Literal && string.IsNullOrEmpty(part.Text))
            {
                continue;
            }

            if (part.Kind == DisplayTextPartKind.Literal && pendingLiteral != null)
            {
                pendingLiteral = DisplayTextPart.Literal(pendingLiteral.Value.Text + part.Text);
                continue;
            }

            if (pendingLiteral != null)
            {
                yield return pendingLiteral.Value;
                pendingLiteral = null;
            }

            if (part.Kind == DisplayTextPartKind.Literal)
            {
                pendingLiteral = part;
            }
            else
            {
                yield return part;
            }
        }

        if (pendingLiteral != null)
        {
            yield return pendingLiteral.Value;
        }
    }
}

internal enum DisplayTextPartKind
{
    Literal,
    Placeholder
}

internal readonly struct DisplayTextPart : IEquatable<DisplayTextPart>
{
    private DisplayTextPart(DisplayTextPartKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public DisplayTextPartKind Kind { get; }
    public string Text { get; }

    public static DisplayTextPart Literal(string text) => new(DisplayTextPartKind.Literal, text);
    public static DisplayTextPart Placeholder() => new(DisplayTextPartKind.Placeholder, string.Empty);

    public bool Equals(DisplayTextPart other)
    {
        return Kind == other.Kind && Text == other.Text;
    }

    public override bool Equals(object? obj)
    {
        return obj is DisplayTextPart other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)Kind * 397) ^ Text.GetHashCode();
        }
    }
}
