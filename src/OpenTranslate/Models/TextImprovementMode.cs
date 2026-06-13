using System.Text.Json.Serialization;

namespace OpenTranslate.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextImprovementMode
{
    None,
    Fix,
    Natural,
    Concise,
    Formal,
    Informal,
    ImproveOnly
}
