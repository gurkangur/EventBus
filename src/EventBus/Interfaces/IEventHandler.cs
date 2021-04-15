using EventBus.Events;
using System.Threading.Tasks;

namespace EventBus.Interfaces
{
    public interface IEventHandler<in TEvent> where TEvent : Event
    {
        Task Handle(TEvent @event);
    }
}
