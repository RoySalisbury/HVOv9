using System.Device.Gpio;
using System.Diagnostics;

namespace HVO.GpioTestApp;

public class GpioLimitSwitch : IDisposable
{
    private readonly PinMode _gpioPinMode;
    private readonly PinMode _eventPinMode;

    private GpioController _gpioController;
    private readonly int _gpioPinNumber;
    private readonly bool _isPullup;
    private readonly bool _shouldDispose;
    private readonly TimeSpan _debounceTime;
    private (DateTime lastEventTimestamp, PinEventTypes lastEventType) _lastEventRecord = new(DateTime.MinValue, PinEventTypes.None);
    private readonly object _objLock = new();

    private bool _disposed = false;

    public GpioLimitSwitch(GpioController gpioController, int gpioPinNumber, bool isPullup = true, bool hasExternalResistor = false, bool shouldDispose = true, TimeSpan debounceTime = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(gpioPinNumber, 0);

        this._gpioController = gpioController ?? new GpioController();
        this._gpioPinNumber = gpioPinNumber;
        this._isPullup = isPullup;
        this.HasExternalResistor = hasExternalResistor;
        this._shouldDispose = gpioController == null ? true : shouldDispose;
        this._debounceTime = debounceTime;

        this._eventPinMode = this._isPullup ? PinMode.InputPullUp : PinMode.InputPullDown;
        this._gpioPinMode = hasExternalResistor ? _gpioPinMode = PinMode.Input : _gpioPinMode = _eventPinMode;

        if (_gpioController.IsPinModeSupported(this._gpioPinNumber, this._gpioPinMode) == false)
        {
            if (this._gpioPinMode == PinMode.Input)
            {
                throw new ArgumentException($"Gpio pin '{this._gpioPinNumber}' can not be configured as Input");
            }

            throw new ArgumentException($"The pin {this._gpioPinNumber} cannot be configured as {(this._isPullup ? "pull-up" : "pull-down")}. Use an external resistor and set {nameof(HasExternalResistor)}=true");
        }

        try
        {
            this._gpioController.OpenPin(this._gpioPinNumber, _gpioPinMode);
            this._gpioController.RegisterCallbackForPinValueChangedEvent(this._gpioPinNumber, PinEventTypes.Falling | PinEventTypes.Rising, PinStateChanged);
        }
        catch (Exception)
        {
            if (this._shouldDispose)
            {
                this._gpioController.Dispose();
            }
        }
    }

public bool HasExternalResistor { get;  private set; }


    private void PinStateChanged(object sneder, PinValueChangedEventArgs pinValueChangedEventArgs)
    {
        var currentTimestamp = DateTime.Now;
        var lastEventRecord = this._lastEventRecord;

        // Handle GPIO debounce
        if (currentTimestamp - lastEventRecord.lastEventTimestamp >= this._debounceTime && (lastEventRecord.lastEventType == PinEventTypes.None || lastEventRecord.lastEventType != pinValueChangedEventArgs.ChangeType))
        {
            if (Monitor.TryEnter(this._objLock, _debounceTime))
            {
                try
                {
                    this._lastEventRecord.lastEventTimestamp = currentTimestamp;
                    this._lastEventRecord.lastEventType = pinValueChangedEventArgs.ChangeType;
                }
                finally
                {
                    Monitor.Exit(this._objLock);
                }
            }
        }
        else
        {
            return;
        }

        // Fire the necessary event handlers
        switch (pinValueChangedEventArgs.ChangeType)
        {
            case PinEventTypes.Falling:
                if (_eventPinMode == PinMode.InputPullUp)
                {
                    HandleLimitEntered(lastEventRecord, currentTimestamp, pinValueChangedEventArgs);
                }
                else
                {
                    HandleLimitExit(lastEventRecord, currentTimestamp, pinValueChangedEventArgs);
                }
                break;
            case PinEventTypes.Rising:
                if (_eventPinMode == PinMode.InputPullUp)
                {
                    HandleLimitExit(lastEventRecord, currentTimestamp, pinValueChangedEventArgs);
                }
                else
                {
                    HandleLimitEntered(lastEventRecord, currentTimestamp, pinValueChangedEventArgs);
                }
                break;
        }
    }

    private void HandleLimitEntered((DateTime lastEventTimestamp, PinEventTypes lastEventType) lastEventRecord, DateTime currentTimestamp, PinValueChangedEventArgs pinValueChangedEventArgs)
    {
        var pinEventRecord = lastEventRecord;
        Console.WriteLine($"LastElapsedMilliseconds: {pinEventRecord.lastEventTimestamp:O}, LastEvent: {pinEventRecord.lastEventType}, CurrentElapsedMilliseconds: {currentTimestamp:O}, CurrentEvent: {pinValueChangedEventArgs.ChangeType}, MS: {(currentTimestamp - lastEventRecord.lastEventTimestamp).TotalMilliseconds}");
    }

    private void HandleLimitExit((DateTime lastEventTimestamp, PinEventTypes lastEventType) lastEventRecord, DateTime currentTimestamp, PinValueChangedEventArgs pinValueChangedEventArgs)
    {
        var pinEventRecord = lastEventRecord;
        Console.WriteLine($"LastElapsedMilliseconds: {pinEventRecord.lastEventTimestamp:O}, LastEvent: {pinEventRecord.lastEventType}, CurrentElapsedMilliseconds: {currentTimestamp:O}, CurrentEvent: {pinValueChangedEventArgs.ChangeType}, MS: {(currentTimestamp - lastEventRecord.lastEventTimestamp).TotalMilliseconds}");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _gpioController.UnregisterCallbackForPinValueChangedEvent(this._gpioPinNumber, PinStateChanged);
            if (this._shouldDispose)
            {
                this._gpioController?.Dispose();
                this._gpioController = null!;
            }
            else
            {
                this._gpioController.ClosePin(this._gpioPinNumber);
            }
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
    }
}