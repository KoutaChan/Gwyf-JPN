using System;
using System.IO;
using Newtonsoft.Json;

namespace GwyfJpn.Plugin;

/// <summary>
/// Append-only JSONL writer for runtime strings missed by static extraction.
/// </summary>
internal sealed class RuntimeUnknownLogger
{
    public static readonly RuntimeUnknownLogger Disabled = new(null);
    private readonly string? _path;
    private readonly object _sync = new();

    public RuntimeUnknownLogger(string? path)
    {
        _path = path;
    }

    public void Write(string? sceneName, string? objectPath, string component, string source)
    {
        if (_path == null)
        {
            return;
        }

        var line = JsonConvert.SerializeObject(new
        {
            timestampUtc = DateTime.UtcNow.ToString("O"),
            scene = sceneName,
            objectPath,
            component,
            source
        });

        lock (_sync)
        {
            File.AppendAllText(_path, line + Environment.NewLine);
        }
    }
}
