using App2.IntegrationEvents.Events;
using EventBus.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace App2.IntegrationEvents.EventHandling
{
    public class ProductPriceChangedIntegrationEventHandler : IEventHandler<ProductPriceChangedIntegrationEvent>
    {
        private readonly ILogger<ProductPriceChangedIntegrationEventHandler> _logger;

        public ProductPriceChangedIntegrationEventHandler(ILogger<ProductPriceChangedIntegrationEventHandler> logger)
        {
            _logger = logger;
        }

        public async Task Handle(ProductPriceChangedIntegrationEvent @event)
        {

            Console.WriteLine(@event.ProductId);
        }
    }
}
