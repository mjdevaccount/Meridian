using System.Net;
using System.Text;
using System.Text.Json;

namespace Meridian.Common.Health;

public class HealthCheckServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<Task<Dictionary<string, object>>> _checkFunc;

    public HealthCheckServer(int port, Func<Task<Dictionary<string, object>>> checkFunc)
    {
        _checkFunc = checkFunc;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.Url?.AbsolutePath == "/health")
                    {
                        var result = await _checkFunc();
                        var json = JsonSerializer.Serialize(result);
                        var buffer = Encoding.UTF8.GetBytes(json);
                        context.Response.ContentType = "application/json";
                        context.Response.StatusCode = 200;
                        await context.Response.OutputStream.WriteAsync(buffer);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                    }
                    context.Response.Close();
                }
                catch (Exception) when (_cts.Token.IsCancellationRequested) { break; }
                catch { }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}
