using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubZeroFramework.Service.Models;

/// <summary>
/// How cooling profiles are written into service-settings.json.
/// </summary>
/// <remarks>
/// PascalCase, matching every other key the configuration binder reads out of that file, and enums as NAMES
/// rather than numbers: this file is one people open and read when something has gone wrong, and
/// <c>"Mode": "Adaptive"</c> answers a question that <c>"Mode": 4</c> only raises. The binder accepts both.
/// </remarks>
public static class CoolingProfileSerialization
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
