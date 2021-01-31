using EventBus.Events;
using System.Threading.Tasks;

namespace EventBus.Interfaces
{
    public interface IIntegrationEventHandler<in TIntegrationEvent> : IIntegrationEventHandler
            where TIntegrationEvent : IntegrationEvent
    {
        Task Handle(TIntegrationEvent @event);
    }

    public interface IIntegrationEventHandler
    {
    }
}
