namespace MedReminder.Infrastructure.Cloud;

// The HttpClient for provider APIs: one per process, pooled connections
// recycled every few minutes so DNS changes are picked up. Tests pass
// their own handler.
public sealed class CloudHttp
{
    public static readonly CloudHttp Shared = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    });

    public CloudHttp(HttpMessageHandler handler)
    {
        Client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
    }

    public HttpClient Client { get; }
}
