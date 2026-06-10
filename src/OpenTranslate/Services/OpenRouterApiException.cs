namespace OpenTranslate.Services;

public sealed class OpenRouterApiException : Exception
{
    public int StatusCode { get; }

    public OpenRouterApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
