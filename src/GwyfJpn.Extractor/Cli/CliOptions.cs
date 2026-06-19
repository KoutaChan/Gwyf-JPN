using System;

namespace GwyfJpn.Extractor;

/// <summary>
/// Parsed command line options. This intentionally stays dependency-free so the
/// extractor remains a small C# command line tool without third-party CLI packages.
/// </summary>
internal sealed class CliOptions
{
    public string? GameDir { get; private set; }
    public string? OutDir { get; private set; }
    public string? Out { get; private set; }
    public string? PseudoOut { get; private set; }
    public string? ExportOut { get; private set; }
    public string? Translations { get; private set; }
    public string? Seen { get; private set; }
    public string? TypeName { get; private set; }
    public string? MethodName { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : null;
            if (value == null || value.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            switch (key)
            {
                case "--game-dir":
                    options.GameDir = value;
                    i++;
                    break;
                case "--out-dir":
                    options.OutDir = value;
                    i++;
                    break;
                case "--out":
                    options.Out = value;
                    i++;
                    break;
                case "--pseudo-out":
                    options.PseudoOut = value;
                    i++;
                    break;
                case "--export-out":
                    options.ExportOut = value;
                    i++;
                    break;
                case "--translations":
                    options.Translations = value;
                    i++;
                    break;
                case "--seen":
                    options.Seen = value;
                    i++;
                    break;
                case "--type":
                    options.TypeName = value;
                    i++;
                    break;
                case "--method":
                    options.MethodName = value;
                    i++;
                    break;
            }
        }

        return options;
    }
}
