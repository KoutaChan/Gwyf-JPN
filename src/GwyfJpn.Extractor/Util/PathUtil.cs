using System;
using System.IO;

namespace GwyfJpn.Extractor;

internal static class PathUtil
{
    public static string RelativeTo(string basePath, string path)
    {
        var baseUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(basePath)));
        var pathUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
    }
}
