using System.Text.Encodings.Web;
using System.Text.Json;

namespace EventBus.EventBusRabbitMq
{
    public class EventBusRabbitMqOptions
    {
        public JsonSerializerOptions JsonSerializerOptions { get; set; } = new JsonSerializerOptions()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        public string HostName { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public string QueueName { get; set; } = "event_bus_queue";

        public string BrokerName { get; set; } = "event_bus";

        public int ConsumerExceptionRetryCount { get; set; } = 5;
        public int RetryCount { get; set; } = 5;
    }
}
