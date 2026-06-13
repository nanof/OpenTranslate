using System.Text.Json.Serialization;

namespace OpenTranslate.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TranslationProvider
{
    MyMemory,
    OpenRouter,
    OpenAi,
    Gemini
}
