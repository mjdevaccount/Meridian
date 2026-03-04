using System.Text.Json;
using Confluent.Kafka;
using Meridian.Common.Models;

namespace Meridian.Dashboard.Services;

public class KafkaCommandPublisher : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    public KafkaCommandPublisher(string bootstrapServers, string topic = "portfolio.commands")
    {
        _topic = topic;
        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(PositionCommand command)
    {
        var json = JsonSerializer.Serialize(command);
        await _producer.ProduceAsync(_topic, new Message<string, string>
        {
            Key = command.Position.PositionId,
            Value = json
        });
    }

    public void Dispose() => _producer.Dispose();
}
