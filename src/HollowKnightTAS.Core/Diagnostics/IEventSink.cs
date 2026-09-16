using System;

namespace HollowKnightTAS.Core.Diagnostics
{
    public interface IEventSink : IDisposable
    {
        void Emit(StructuredEvent value);
        void Flush(TimeSpan timeout);
    }
}

