using EventBus.Events;
using EventBus.Extensions;
using EventBus.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EventBus.EventBusRabbitMq
{
    public class EventBusRabbitMq : IEventBus, IDisposable
    {
        private readonly IRabbitMqPersistentConnection _persistentConnection;
        private readonly ILogger<EventBusRabbitMq> _logger;
        private readonly IEventBusSubscriptionsManager _subsManager;
        private IModel _consumerChannel;
        private readonly string _exchangeType;

        private readonly EventBusRabbitMqOptions _options;

        private readonly IServiceProvider _services;

        public EventBusRabbitMq(
            IServiceProvider services,
            IOptions<EventBusRabbitMqOptions> options,
            IRabbitMqPersistentConnection persistentConnection, 
            ILogger<EventBusRabbitMq> logger,
            IEventBusSubscriptionsManager subsManager)
        {
            _logger = logger;
            _services = services;
            _persistentConnection =
                persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
            _subsManager = subsManager ?? new EventBusSubscriptionsManager();

            _options = options.Value;
            _exchangeType = "direct";
            _consumerChannel = CreateConsumerChannel();
            _subsManager.OnEventRemoved += SubsManager_OnEventRemoved;
        }

        private void SubsManager_OnEventRemoved(object sender, string eventName)
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            using (var channel = _persistentConnection.CreateModel())
            {
                channel.QueueUnbind(queue: _options.QueueName,
                    exchange: _options.BrokerName,
                    routingKey: eventName);
                if (_subsManager.IsEmpty)
                {
                    _consumerChannel.Close();
                }
            }
        }

        public void Publish(Event @event)
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            var policy = Policy.Handle<BrokerUnreachableException>()
                .Or<SocketException>()
                .WaitAndRetry(_options.RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (ex, time) =>
                    {
                        _logger.LogWarning(ex,
                            "Could not publish event: {EventId} after {Timeout}s ({ExceptionMessage})", @event.Id,
                            $"{time.TotalSeconds:n1}", ex.Message);
                    });
            using (var channel = _persistentConnection.CreateModel())
            {
                var eventName = _subsManager.GetEventKey(@event);
                channel.ExchangeDeclare(exchange: _options.BrokerName, type: _exchangeType);

                var message = JsonSerializer.Serialize(@event, @event.GetType(), _options.JsonSerializerOptions);
                var body = Encoding.UTF8.GetBytes(message);

                policy.Execute(() =>
                {
                    var properties = channel.CreateBasicProperties();
                    properties.DeliveryMode = 2; // persistent
                    channel.BasicPublish(exchange: _options.BrokerName,
                        routingKey: eventName,
                        mandatory: true,
                        basicProperties: properties,
                        body: body);
                });
            }
        }

        public void SubscribeDynamic<TH>(string eventName)
            where TH : IDynamicEventHandler
        {
            DoInternalSubscription(eventName);

            _logger.LogInformation("Subscribing to dynamic event {EventName} with {EventHandler}", eventName,
                typeof(TH).GetGenericTypeName());
            _subsManager.AddDynamicSubscription<TH>(eventName);
            StartBasicConsume();
        }

        public void Subscribe<T, TH>()
            where T : Event
            where TH : IEventHandler<T>
        {
            var eventName = _subsManager.GetEventKey<T>();
            DoInternalSubscription(eventName);

            _logger.LogInformation("Subscribing to event {EventName} with {EventHandler}", eventName,
                typeof(TH).GetGenericTypeName());
            _subsManager.AddSubscription<T, TH>();
            StartBasicConsume();
        }

        private void DoInternalSubscription(string eventName)
        {
            var containsKey = _subsManager.HasSubscriptionsForEvent(eventName);
            if (!containsKey)
            {
                if (!_persistentConnection.IsConnected)
                {
                    _persistentConnection.TryConnect();
                }

                using (var channel = _persistentConnection.CreateModel())
                {
                    channel.QueueBind(queue: _options.QueueName,
                        exchange: _options.BrokerName,
                        routingKey: eventName);
                }
            }
        }

        public void Unsubscribe<T, TH>()
            where TH : IEventHandler<T>
            where T : Event
        {
            var eventName = _subsManager.GetEventKey<T>();
            _logger.LogInformation("Unsubscribing from event {EventName}", eventName);
            _subsManager.RemoveSubscription<T, TH>();
        }

        public void UnsubscribeDynamic<TH>(string eventName)
            where TH : IDynamicEventHandler
        {
            _logger.LogInformation("Unsubscribing from event {EventName}", eventName);
            _subsManager.RemoveDynamicSubscription<TH>(eventName);
        }

        public void Dispose()
        {
            _consumerChannel?.Dispose();
            _subsManager.Clear();
        }

        private void StartBasicConsume()
        {
            _logger.LogTrace("Starting RabbitMQ basic consume");

            if (_consumerChannel != null)
            {
                var consumer = new EventingBasicConsumer(_consumerChannel);

                consumer.Received += Consumer_Received;

                _consumerChannel.BasicConsume(
                    queue: _options.QueueName,
                    autoAck: false,
                    consumer: consumer);
            }
            else
            {
                _logger.LogError("StartBasicConsume can't call on _consumerChannel == null");
            }
        }

        private async void Consumer_Received(object model, BasicDeliverEventArgs ea)
        {
            var eventName = ea.RoutingKey;
            var message = Encoding.UTF8.GetString(ea.Body.Span);
            var processed = false;

            try
            {
                if (message.ToLowerInvariant().Contains("throw-fake-exception"))
                {
                    throw new InvalidOperationException($"Fake exception requested: \"{message}\"");
                }

                processed = await ProcessEvent(eventName, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "----- ERROR Processing message \"{Message}\"", message);
            }

            if (processed)
            {
                _consumerChannel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            else
            {
                int retryCount = GetRetryCount(ea.BasicProperties, "number-of-retries");

                if (retryCount < _options.ConsumerExceptionRetryCount)
                {
                    _logger.LogWarning("Message {0} has thrown an exception. Current number of retries: {1}", message, retryCount);
                    IBasicProperties propertiesForCopy = _consumerChannel.CreateBasicProperties();
                    IDictionary<string, object> headersCopy = CopyHeaders(ea.BasicProperties);
                    propertiesForCopy.Headers = headersCopy;
                    propertiesForCopy.Headers["number-of-retries"] = ++retryCount;
                    var eventType = _subsManager.GetEventTypeByName(eventName);
                    var integrationEvent = JsonSerializer.Deserialize(message, eventType, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) as Event;
                    integrationEvent.RetryCount = retryCount;
                    var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, eventType, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    _consumerChannel.BasicPublish(ea.Exchange, ea.RoutingKey, propertiesForCopy, ea.Body);
                    _consumerChannel.BasicAck(ea.DeliveryTag, false);
                    _logger.LogWarning("Message {0} thrown back at queue for retry. New retry count: {1}", message, retryCount);
                }
                else //must be rejected, cannot process
                {
                    _logger.LogError("Message {0} has reached the max number of retries. It will be rejected.", message);
                    _consumerChannel.BasicReject(ea.DeliveryTag, false);
                }
            }

        }
        private static IDictionary<string, object> CopyHeaders(IBasicProperties originalProperties)
        {
            IDictionary<string, object> dict = new Dictionary<string, object>();
            IDictionary<string, object> headers = originalProperties.Headers;
            if (headers != null)
            {
                foreach (KeyValuePair<string, object> kvp in headers)
                {
                    dict[kvp.Key] = kvp.Value;
                }
            }

            return dict;
        }
        private static int GetRetryCount(IBasicProperties messageProperties, string countHeader)
        {
            IDictionary<string, object> headers = messageProperties.Headers;
            int count = 0;
            if (headers != null)
            {
                if (headers.ContainsKey(countHeader))
                {
                    string countAsString = Convert.ToString(headers[countHeader]);
                    count = Convert.ToInt32(countAsString);
                }
            }

            return count;
        }

        private IModel CreateConsumerChannel()
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }

            _logger.LogTrace("Creating RabbitMQ consumer channel");
            var channel = _persistentConnection.CreateModel();

            channel.ExchangeDeclare(exchange: _options.BrokerName,
                type: _exchangeType);
            channel.QueueDeclare(queue: _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);
            _logger.LogInformation($"Queue [{_options.QueueName}] declared");

            channel.CallbackException += (sender, ea) =>
            {
                _logger.LogWarning(ea.Exception, "Recreating RabbitMQ consumer channel");
                _consumerChannel.Dispose();
                _consumerChannel = CreateConsumerChannel();
                StartBasicConsume();
            };

            _logger.LogInformation("Channel created!");

            return channel;
        }

        private async Task<bool> ProcessEvent(string eventName, string message)
        {
            var processed = false;
            if (_subsManager.HasSubscriptionsForEvent(eventName))
            {
                using (var scope = _services.CreateScope())
                {
                    var subscriptions = _subsManager.GetHandlersForEvent(eventName);
                    foreach (var subscription in subscriptions)
                    {
                        if (subscription.IsDynamic)
                        {
                            if (!(scope.ServiceProvider.GetRequiredService(subscription.HandlerType) is
                                IDynamicEventHandler handler))
                            {
                                throw new NullReferenceException(
                                    $"Cannot find EventHandler, type {subscription.HandlerType.Name}");
                            }

                            dynamic eventData =
                                JsonSerializer.Deserialize<ExpandoObject>(message, _options.JsonSerializerOptions);

                            await handler.Handle(eventData);
                        }
                        else
                        {
                            var eventType = _subsManager.GetEventTypeByName(eventName);
                            var integrationEvent =
                                JsonSerializer.Deserialize(message, eventType, _options.JsonSerializerOptions);
                            var handler = scope.ServiceProvider.GetRequiredService(subscription.HandlerType);
                            var concreteType = typeof(IEventHandler<>).MakeGenericType(eventType);

                            // ReSharper disable once PossibleNullReferenceException
                            await (Task)concreteType.GetMethod("Handle").Invoke(handler, new[] { integrationEvent });
                        }
                    }
                }

                processed = true;
            }
            else
            {
                _logger.LogWarning("No subscription for RabbitMQ event: {EventName}", eventName);
            }

            return processed;
        }
    }
}
