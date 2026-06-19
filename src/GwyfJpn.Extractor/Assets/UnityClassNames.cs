namespace GwyfJpn.Extractor;

/// <summary>Readable names for the Unity class ids used as extraction context.</summary>
internal static class UnityClassNames
{
    public static string GetName(int classId)
    {
        return classId switch
        {
            49 => "TextAsset",
            114 => "MonoBehaviour",
            _ => $"UnityClass:{classId}"
        };
    }
}
