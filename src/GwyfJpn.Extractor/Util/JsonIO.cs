using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GwyfJpn.Extractor;

internal static class JsonIO
{
    public static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        var serializer = new DataContractJsonSerializer(value.GetType());
        serializer.WriteObject(stream, value);
    }

    public static void WriteIndentedJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var serialized = new MemoryStream();
        var serializer = new DataContractJsonSerializer(value.GetType());
        serializer.WriteObject(serialized, value);
        serialized.Position = 0;
        using var document = JsonDocument.Parse(serialized);
        using var output = File.Create(path);
        using var writer = new Utf8JsonWriter(
            output,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = true
            });
        document.WriteTo(writer);
    }

    public static T ReadJson<T>(string path)
    {
        using var stream = File.OpenRead(path);
        var serializer = new DataContractJsonSerializer(typeof(T));
        return (T)(serializer.ReadObject(stream) ?? throw new InvalidOperationException($"Could not parse JSON: {path}"));
    }
}
