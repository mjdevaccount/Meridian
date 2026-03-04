using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Confluent.Kafka;
using Meridian.Common.Models;

namespace Meridian.Common.Messaging;

public class PortfolioCommandConsumer : IDisposable
{
    private readonly Subject<PositionCommand> _commandSubject = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _consumerThread;

    public PortfolioCommandConsumer(string bootstrapServers, string topic = "portfolio.commands", string groupId = "meridian-commands")
    {
        _consumerThread = new Thread(() => ConsumeLoop(bootstrapServers, topic, groupId))
        {
            IsBackground = true,
            Name = $"PortfolioCommandConsumer-{groupId}"
        };
        _consumerThread.Start();
    }

    public IObservable<PositionCommand> Commands => _commandSubject.AsObservable();

    private void ConsumeLoop(string bootstrapServers, string topic, string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (result?.Message?.Value == null) continue;

                var command = JsonSerializer.Deserialize<PositionCommand>(result.Message.Value, jsonOptions);
                if (command != null)
                {
                    _commandSubject.OnNext(command);
                }
            }
            catch (ConsumeException)
            {
                // Skip bad messages
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _commandSubject.OnCompleted();
        _commandSubject.Dispose();
        _cts.Dispose();
    }
}
