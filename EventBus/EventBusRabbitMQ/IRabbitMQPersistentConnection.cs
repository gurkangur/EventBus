using RabbitMQ.Client;
using System;

namespace EventBus.EventBusRabbitMQ
{
    public interface IRabbitMQPersistentConnection
           : IDisposable
    {
        bool IsConnected { get; }

        bool TryConnect();

        IModel CreateModel();
    }
}
