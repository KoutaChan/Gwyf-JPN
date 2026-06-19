using System;
using System.IO;
using Newtonsoft.Json;

namespace GwyfJpn.Plugin;

/// <summary>
/// Append-only JSONL writer for every English string that reached a display sink.
/// This is the simple extraction path: Unity and the game decide what is display text.
/// </summary>
internal sealed class RuntimeSeenLogger
{
    public static readonly RuntimeSeenLogger Disabled = new(null);
    private readonly string? _path;
    private readonly object _sync = new();

    public RuntimeSeenLogger(string? path)
    {
        _path = path;
    }

    public bool Enabled => _path != null;

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
