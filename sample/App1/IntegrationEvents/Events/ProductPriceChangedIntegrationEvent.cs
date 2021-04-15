using EventBus.Events;

namespace App1.IntegrationEvents.Events
{
    public class ProductPriceChangedIntegrationEvent : Event
    {
        public int ProductId { get; private init; }
        public decimal NewPrice { get; private init; }
        public decimal OldPrice { get; private init; }

        public ProductPriceChangedIntegrationEvent(int productId, decimal newPrice, decimal oldPrice)
        {
            ProductId = productId;
            NewPrice = newPrice;
            OldPrice = oldPrice;
        }
    }
}
