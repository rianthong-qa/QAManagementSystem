using System.Net;

namespace ProMaxx2.Automation.AgentGui;

internal sealed class HubRecoveryHandler : DelegatingHandler
{
    public HubRecoveryHandler() : base(new HttpClientHandler()) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get) return await base.SendAsync(request, cancellationToken);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (response.StatusCode != HttpStatusCode.RequestTimeout && (int)response.StatusCode != 502 && (int)response.StatusCode != 503 && (int)response.StatusCode != 504 || attempt >= 2) return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < 2)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
        }
    }
}
