using EventBus.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventBus.IntegrationEvents.Events
{
    public record ProductPriceChangedIntegrationEvent : IntegrationEvent
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
