using System.Device.Gpio;
using System.Threading.Tasks;

namespace HVO.GpioTestApp;

class Program
{
    const int Pin = 21;
    const string Alert = "ALERT 🚨";
    const string Ready = "READY ✅";

    static void Main(string[] args)
    {
        using var controller = new GpioController();
        controller.OpenPin(Pin, PinMode.InputPullUp);

        Console.WriteLine(
            $"Initial status ({DateTime.Now}): {(controller.Read(Pin) == PinValue.High ? Alert : Ready)}");

        controller.RegisterCallbackForPinValueChangedEvent(
            Pin,
            PinEventTypes.Falling | PinEventTypes.Rising,
            OnPinEvent);

        Console.ReadLine();
    }

    static void OnPinEvent(object sender, PinValueChangedEventArgs args)
    {
        Console.WriteLine(
            $"({DateTime.Now}) {(args.ChangeType is PinEventTypes.Rising ? Alert : Ready)}");
    }
}

