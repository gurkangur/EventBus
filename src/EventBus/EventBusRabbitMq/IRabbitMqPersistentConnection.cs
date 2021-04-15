using System;
using RabbitMQ.Client;

namespace EventBus.EventBusRabbitMq
{
    public interface IRabbitMqPersistentConnection
           : IDisposable
    {
        bool IsConnected { get; }

        bool TryConnect();

        IModel CreateModel();
    }
}
