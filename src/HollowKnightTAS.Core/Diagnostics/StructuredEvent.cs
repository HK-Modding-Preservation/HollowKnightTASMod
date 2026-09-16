using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HollowKnightTAS.Core.Diagnostics
{
    public sealed class StructuredEvent
    {
        public StructuredEvent(
            int schemaVersion,
            string sessionId,
            long sequence,
            string eventType,
            DateTimeOffset timestampUtc,
            IReadOnlyDictionary<string, string> fields)
        {
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("A session ID is required.", nameof(sessionId));
            }

            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            if (string.IsNullOrWhiteSpace(eventType))
            {
                throw new ArgumentException("An event type is required.", nameof(eventType));
            }

            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            var copiedFields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in fields)
            {
                if (string.IsNullOrWhiteSpace(item.Key) || item.Value == null)
                {
                    throw new ArgumentException("Event fields require non-empty keys and non-null values.", nameof(fields));
                }

                copiedFields.Add(item.Key, item.Value);
            }

            SchemaVersion = schemaVersion;
            SessionId = sessionId;
            Sequence = sequence;
            EventType = eventType;
            TimestampUtc = timestampUtc.ToUniversalTime();
            Fields = new ReadOnlyDictionary<string, string>(copiedFields);
        }

        public int SchemaVersion { get; }
        public string SessionId { get; }
        public long Sequence { get; }
        public string EventType { get; }
        public DateTimeOffset TimestampUtc { get; }
        public IReadOnlyDictionary<string, string> Fields { get; }
    }
}

