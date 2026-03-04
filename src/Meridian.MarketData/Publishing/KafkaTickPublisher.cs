using System.Text.Json;
using Confluent.Kafka;
using Meridian.Common.Models;

namespace Meridian.MarketData.Publishing;

public class KafkaTickPublisher : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _ticksTopic;
    private readonly string _volSurfaceTopic;

    public KafkaTickPublisher(string bootstrapServers, string ticksTopic, string volSurfaceTopic)
    {
        _ticksTopic = ticksTopic;
        _volSurfaceTopic = volSurfaceTopic;

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.Leader,
            LingerMs = 5, // small batching for throughput
            BatchSize = 16384
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public void PublishTick(MarketTick tick)
    {
        var json = JsonSerializer.Serialize(tick);
        _producer.Produce(_ticksTopic, new Message<string, string>
        {
            Key = tick.Symbol,
            Value = json
        }, report =>
        {
            if (report.Error.IsError)
                Console.Error.WriteLine($"Kafka produce error: {report.Error.Reason}");
        });
    }

    public void PublishVolSurface(VolSurfaceUpdate update)
    {
        var json = JsonSerializer.Serialize(update);
        _producer.Produce(_volSurfaceTopic, new Message<string, string>
        {
            Key = update.Symbol,
            Value = json
        });
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
