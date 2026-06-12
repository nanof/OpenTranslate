using System.Text.Json.Serialization;

namespace OpenTranslate.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TranslationProvider
{
    OpenRouter,
    OpenAi,
    Gemini
}
