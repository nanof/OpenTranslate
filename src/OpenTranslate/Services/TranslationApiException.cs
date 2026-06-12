namespace OpenTranslate.Services;

public sealed class TranslationApiException : Exception
{
    public int StatusCode { get; }

    public TranslationApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
