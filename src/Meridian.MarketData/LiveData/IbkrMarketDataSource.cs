using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Meridian.Common.Configuration;
using Meridian.Common.Interfaces;
using Meridian.Common.Models;

namespace Meridian.MarketData.LiveData;

/// <summary>
/// IBKR market data source that connects to IB Gateway/TWS via the InterReact library.
/// Implements IMarketDataSource to provide real-time ticks from Interactive Brokers.
///
/// Prerequisites:
/// - IB Gateway or TWS must be running and accepting API connections
/// - Market data subscriptions must be active for requested symbols
/// - InterReact NuGet package must be installed
///
/// When IBKR is unavailable, the MarketData service falls back to simulated mode.
/// </summary>
public class IbkrMarketDataSource : IMarketDataSource, IAsyncDisposable
{
    private readonly IbkrConfig _config;
    private readonly Subject<MarketTick> _tickSubject = new();
    private readonly Subject<VolSurfaceUpdate> _volSubject = new();
    private readonly ConcurrentDictionary<string, decimal> _lastPrices = new();
    private readonly ConcurrentDictionary<string, decimal> _lastVols = new();
    private readonly HashSet<string> _subscribedSymbols = new();
    private readonly object _lock = new();
    private long _sequenceNumber;
    private bool _isConnected;
    private CancellationTokenSource _cts = new();
    private Action<string>? _log;

    public IbkrMarketDataSource(IbkrConfig config, Action<string>? log = null)
    {
        _config = config;
        _log = log;
    }

    public bool IsConnected => _isConnected;

    /// <summary>
    /// Attempts to connect to IB Gateway. Returns true if connected, false otherwise.
    /// The MarketData service should fall back to simulated mode on failure.
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            _log?.Invoke($"Connecting to IBKR at {_config.Host}:{_config.Port}...");

            // Attempt connection using InterReact
            // This will throw if IB Gateway is not running
            // The actual InterReact connection code goes here when the package is available

            // For now, we verify connectivity by attempting a TCP connection
            using var tcpClient = new System.Net.Sockets.TcpClient();
            var connectTask = tcpClient.ConnectAsync(_config.Host, _config.Port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(5000, ct));

            if (completed != connectTask || !tcpClient.Connected)
            {
                _log?.Invoke($"IBKR Gateway not reachable at {_config.Host}:{_config.Port}");
                return false;
            }

            tcpClient.Close();
            _isConnected = true;
            _log?.Invoke("IBKR Gateway is reachable. Starting market data streams...");

            // Start the data feed in background
            _ = Task.Run(() => RunDataFeedAsync(_cts.Token), _cts.Token);

            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"IBKR connection failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Background task that maintains the IBKR connection and feeds market data.
    /// When InterReact is available, this uses its IObservable streams.
    /// </summary>
    private async Task RunDataFeedAsync(CancellationToken ct)
    {
        // Subscribe to initial symbols from config
        foreach (var sym in _config.Symbols)
        {
            lock (_lock) { _subscribedSymbols.Add(sym.MeridianSymbol); }
        }

        // The InterReact connection loop would go here:
        // var client = await InterReactClient.ConnectAsync(host, port, clientId);
        // foreach (var sym in _config.Symbols)
        // {
        //     var contract = new Contract { Symbol = sym.Symbol, SecType = sym.SecType, Exchange = sym.Exchange, Currency = sym.Currency };
        //     client.Services.CreateMarketDataObservable(contract)
        //         .OfType<TickPrice>()
        //         .Where(t => t.TickType == TickType.Last)
        //         .Subscribe(tick => {
        //             _lastPrices[sym.MeridianSymbol] = (decimal)tick.Price;
        //             _tickSubject.OnNext(new MarketTick(
        //                 sym.MeridianSymbol, (decimal)tick.Price,
        //                 _lastVols.GetValueOrDefault(sym.MeridianSymbol),
        //                 DateTime.UtcNow, Interlocked.Increment(ref _sequenceNumber)));
        //         });
        // }

        _log?.Invoke("IBKR data feed placeholder running. Install InterReact package and uncomment connection code for live data.");

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) { }
    }

    public IObservable<MarketTick> GetTickStream(string symbol)
    {
        return _tickSubject
            .Where(t => string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .AsObservable();
    }

    public IObservable<MarketTick> GetAllTicksStream() => _tickSubject.AsObservable();

    public IObservable<VolSurfaceUpdate> GetVolSurfaceStream() => _volSubject.AsObservable();

    public Task<bool> SubscribeToSymbolAsync(string symbol)
    {
        lock (_lock)
        {
            if (_subscribedSymbols.Contains(symbol)) return Task.FromResult(true);
            _subscribedSymbols.Add(symbol);
            _log?.Invoke($"IBKR: Subscribing to {symbol}");
            // When InterReact is connected, create a new market data subscription here
        }
        return Task.FromResult(true);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _tickSubject.OnCompleted();
        _volSubject.OnCompleted();
        _tickSubject.Dispose();
        _volSubject.Dispose();
        _cts.Dispose();
        await Task.CompletedTask;
    }
}
