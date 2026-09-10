using System.Text.Json;
using System.Text.Json.Serialization;

namespace RocketIDE.Rocket.LanguageServer.LspDtos;

public static class LspJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
    };
}
