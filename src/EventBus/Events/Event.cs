using System;
using System.Text.Json.Serialization;

namespace EventBus.Events
{
    public class Event
    {
        public Event()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;
            RetryCount = 1;
        }

        [JsonConstructor]
        public Event(Guid id, DateTime createDate, int retryCount)
        {
            Id = id;
            CreationDate = createDate;
            RetryCount = retryCount;
        }
        [JsonInclude]
        public Guid Id { get; private set; }
        [JsonInclude]
        public DateTime CreationDate { get; private set; }
        [JsonInclude]
        public int RetryCount { get; set; }
    }
}
