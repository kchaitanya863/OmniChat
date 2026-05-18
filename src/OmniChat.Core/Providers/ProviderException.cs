using System.Net;
using OmniChat.Core.Models;

namespace OmniChat.Core.Providers;

public sealed class ProviderException : Exception
{
    public ProviderException(ProviderType providerType, HttpStatusCode statusCode, string message)
        : base(message)
    {
        ProviderType = providerType;
        StatusCode = statusCode;
    }

    public ProviderException(ProviderType providerType, HttpStatusCode statusCode, string message, Exception inner)
        : base(message, inner)
    {
        ProviderType = providerType;
        StatusCode = statusCode;
    }

    public ProviderType ProviderType { get; }

    public HttpStatusCode StatusCode { get; }
}
