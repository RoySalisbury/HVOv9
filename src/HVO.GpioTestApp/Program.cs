using System.Collections.Concurrent;
using System.Diagnostics;
using System.Device.Gpio;
using Iot.Device.Button;
using System.Linq.Expressions;

namespace HVO.GpioTestApp;

public struct PinEventRecord
{
    public PinEventRecord(Stopwatch stopwatch, PinEventTypes lastPinEventType)
    {
        Stopwatch = stopwatch;
        LastPinEventType = lastPinEventType;
    }

    public Stopwatch Stopwatch { get; private set; }
    public PinEventTypes LastPinEventType { get; private set; }

    public void Update(Stopwatch stopwatch, PinEventTypes lastPinEventType)
    {
        Stopwatch = stopwatch;
        LastPinEventType = lastPinEventType;
    }
}

class Program3
{
    private static readonly GpioController gpioController = new GpioController();
    private static readonly ConcurrentDictionary<int, PinEventRecord> pinEventRecords = [];
    private const int DEBOUNCE_MILLISECONDS = 20;

    static void Main2(string[] args)
    {
        using var pin16 = gpioController.OpenPin(16, PinMode.InputPullUp);

        _ = pinEventRecords.TryAdd(pin16.PinNumber, new PinEventRecord(new Stopwatch(), PinEventTypes.None));
        pin16.ValueChanged += OnPinEvent;

        Console.ReadLine();
    }

    static void OnPinEvent(object sender, PinValueChangedEventArgs args)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        pinEventRecords.AddOrUpdate(
            args.PinNumber,
            key => new PinEventRecord(stopwatch, args.ChangeType),
            (key, record) =>
            {
                if (stopwatch.ElapsedMilliseconds - record.Stopwatch.ElapsedMilliseconds >= DEBOUNCE_MILLISECONDS &&
                    (record.LastPinEventType == PinEventTypes.None || record.LastPinEventType != args.ChangeType))
                {
                    LogPinEvent(record, stopwatch, args);
                    return new PinEventRecord(stopwatch, args.ChangeType);
                }
                else
                {
                    return record;
                }
            });
    }

    private static void LogPinEvent(PinEventRecord pinEventRecord, Stopwatch stopwatch, PinValueChangedEventArgs args)
    {
        Console.WriteLine($"LastElapsedMilliseconds: {pinEventRecord.Stopwatch.ElapsedMilliseconds}, LastEvent: {pinEventRecord.LastPinEventType}, CurrentElapsedMilliseconds: {stopwatch.ElapsedMilliseconds}, CurrentEvent: {args.ChangeType}, MS: {(stopwatch.ElapsedMilliseconds - pinEventRecord.Stopwatch.ElapsedMilliseconds)}");
    }
}

class Program2
{
    static void Main(string[] args)
    {
        using var pin16 = new GpioLimitSwitch(null!, 16, debounceTime: TimeSpan.FromMilliseconds(50));
        Console.ReadLine();
    }
}
