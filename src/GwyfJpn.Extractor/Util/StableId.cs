namespace GwyfJpn.Extractor;

internal static class StableId
{
    public static string Hash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash.ToString("x8");
        }
    }
}
