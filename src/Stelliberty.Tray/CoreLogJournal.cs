using Stelliberty.Infrastructure.Tray;
using Stelliberty.Domain.CoreLogs;

namespace Stelliberty.Tray;

internal sealed class CoreLogJournal
{
    internal const int Capacity = 2000;

    private readonly object _gate = new();
    private readonly Queue<TrayCoreLogEntry> _entries = new(Capacity);
    private long _sequence;

    public long LatestSequence
    {
        get
        {
            lock (_gate)
            {
                return _sequence;
            }
        }
    }

    public TrayCoreLogEntry Append(long coreGeneration, CoreLogMessage message)
    {
        lock (_gate)
        {
            var entry = new TrayCoreLogEntry(++_sequence, coreGeneration, message);
            if (_entries.Count >= Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
            return entry;
        }
    }

    public TrayCoreLogBatch ReadAfter(long afterSequence)
    {
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                return new TrayCoreLogBatch([], 0, _sequence, false);
            }

            var oldest = _entries.Peek().Sequence;
            var hasGap = afterSequence < oldest - 1;
            var entries = hasGap
                ? _entries.ToArray()
                : _entries.Where(entry => entry.Sequence > afterSequence).ToArray();
            return new TrayCoreLogBatch(entries, oldest, _sequence, hasGap);
        }
    }
}
