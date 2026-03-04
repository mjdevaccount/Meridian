using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Confluent.Kafka;
using Meridian.Common.Interfaces;
using Meridian.Common.Models;

namespace Meridian.Pricing.Streams;

public class MarketDataStream : IMarketDataSource, IDisposable
{
    private readonly Subject<MarketTick> _tickSubject = new();
    private readonly Subject<VolSurfaceUpdate> _volSubject = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _tickConsumerThread;
    private readonly Thread _volConsumerThread;

    public MarketDataStream(string bootstrapServers, string ticksTopic, string volSurfaceTopic, string groupId = "meridian-pricing")
    {
        var tickConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true
        };

        var volConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId + "-vol",
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true
        };

        _tickConsumerThread = new Thread(() => ConsumeLoop<MarketTick>(tickConfig, ticksTopic, _tickSubject))
        {
            IsBackground = true,
            Name = "KafkaTickConsumer"
        };

        _volConsumerThread = new Thread(() => ConsumeLoop<VolSurfaceUpdate>(volConfig, volSurfaceTopic, _volSubject))
        {
            IsBackground = true,
            Name = "KafkaVolConsumer"
        };

        _tickConsumerThread.Start();
        _volConsumerThread.Start();
    }

    private void ConsumeLoop<T>(ConsumerConfig config, string topic, Subject<T> subject)
    {
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(_cts.Token);
                    if (result?.Message?.Value != null)
                    {
                        var item = JsonSerializer.Deserialize<T>(result.Message.Value);
                        if (item != null)
                            subject.OnNext(item);
                    }
                }
                catch (ConsumeException ex)
                {
                    Console.Error.WriteLine($"Kafka consume error on {topic}: {ex.Error.Reason}");
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Close();
        }
    }

    public IObservable<MarketTick> GetTickStream(string symbol)
    {
        return _tickSubject
            .Where(t => string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .AsObservable();
    }

    public IObservable<MarketTick> GetAllTicksStream()
    {
        return _tickSubject.AsObservable();
    }

    public IObservable<VolSurfaceUpdate> GetVolSurfaceStream()
    {
        return _volSubject.AsObservable();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _tickSubject.Dispose();
        _volSubject.Dispose();
        _cts.Dispose();
    }
}
