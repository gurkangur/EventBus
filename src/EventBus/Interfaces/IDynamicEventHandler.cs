using System.Threading.Tasks;


namespace EventBus.Interfaces
{
    public interface IDynamicEventHandler
    {
        Task Handle(dynamic eventData);
    }
}
