using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loyalty.Core.Persistence;

public static class JsonOptionsProvider
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
