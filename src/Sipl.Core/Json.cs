using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Sipl;

public static class Json
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions Compact = new(Indented) { WriteIndented = false };

    public static JsonNode Number(BigInteger value) => JsonNode.Parse(value.ToString(CultureInfo.InvariantCulture))!;

    public static JsonObject State(IEnumerable<KeyValuePair<string, BigInteger>> state)
    {
        var obj = new JsonObject();
        foreach (var (k, v) in state)
        {
            obj[k] = Number(v);
        }

        return obj;
    }

    public static string Serialize(JsonNode? node, bool indented = true) =>
        node is null ? "null" : node.ToJsonString(indented ? Indented : Compact);
}
