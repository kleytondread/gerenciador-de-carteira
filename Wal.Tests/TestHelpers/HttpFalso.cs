using System.Net;
using System.Text;

namespace Wal.Tests.TestHelpers;

/// <summary>Responde às requisições HTTP com o que o teste mandar e guarda as URLs chamadas.</summary>
internal sealed class HandlerFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<Uri> Requisicoes { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requisicoes.Add(request.RequestUri!);
        return Task.FromResult(responder(request));
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
