using System.Device.I2c;
using HVO.Iot.Devices.Iot.Devices.Sequent;
using Iot.Device.OneWire;

namespace HVO.CLI.RoofController;

class Program
{
    static void Main(string[] args)
    {
        // var settings1 = new I2cConnectionSettings(1, 0x0e);
        // using var device1 = I2cDevice.Create(settings1);
        // using var sequentRelayHat = new FourRelayFourInputHat(device1);
        // Console.WriteLine("Relay Hardware Revision: " + sequentRelayHat.HardwareRevision);
        // Console.WriteLine("Relay Software Revision: " + sequentRelayHat.SoftwareRevision);

        var voltageIn = Wdt.GetVin();
        Console.WriteLine("Vin: " + voltageIn);

        var voltageBat = Wdt.GetVbat();
        Console.WriteLine("Vbat: " + voltageBat);

        var tempC = Wdt.GetTemp();
        Console.WriteLine("TempC: " + tempC);

        var chargeStat = Wdt.GetChargeStat();
        Console.WriteLine("ChargeStat: " + chargeStat);

        var vRasp = Wdt.GetVrasp();
        Console.WriteLine("VRasp: " + vRasp);




//         var ledMode1 = sequentRelayHat.GetLedMode(1);
        // sequentRelayHat.SetLedMode(1, Iot.Devices.Iot.Devices.Sequent.FourRelayHat.LED_MODE.MANUAL);
        // ledMode1 = sequentRelayHat.GetLedMode(1);

        // var ledState1 = sequentRelayHat.GetLedState(1);
        // sequentRelayHat.SetLedState(1, Iot.Devices.Iot.Devices.Sequent.FourRelayHat.LED_STATE.ON);
        // ledState1 = sequentRelayHat.GetLedState(1);
        // sequentRelayHat.SetLedState(1, Iot.Devices.Iot.Devices.Sequent.FourRelayHat.LED_STATE.OFF);
        // ledState1 = sequentRelayHat.GetLedState(1);

        // sequentRelayHat.SetLedMode(1, Iot.Devices.Iot.Devices.Sequent.FourRelayHat.LED_MODE.AUTO);
        // ledMode1 = sequentRelayHat.GetLedMode(1);
        // sequentRelayHat.SetLedState(1, Iot.Devices.Iot.Devices.Sequent.FourRelayHat.LED_STATE.ON);
        // ledState1 = sequentRelayHat.GetLedState(1);
        // sequentRelayHat.SetLedState(1, Iot.Devices.Iot.Devices.Sequent.FourRelayHat.LED_STATE.OFF);
        // ledState1 = sequentRelayHat.GetLedState(1);



        // sequentRelayHat.SetRelayState(1, true);
        // sequentRelayHat.SetRelayState(2, true);
        // sequentRelayHat.SetRelayState(3, true);
        // sequentRelayHat.SetRelayState(4, true);
        // System.Threading.Thread.Sleep(1000);

        // var relay1 = sequentRelayHat.GetRelayState(1);
        // var relay2 = sequentRelayHat.GetRelayState(2);
        // var relay3 = sequentRelayHat.GetRelayState(3);
        // var relay4 = sequentRelayHat.GetRelayState(4);
        // System.Threading.Thread.Sleep(1000);


        // sequentRelayHat.SetRelayState(1, false);
        // relay1 = sequentRelayHat.GetRelayState(1);
        // sequentRelayHat.SetRelayState(2, false);
        // relay2 = sequentRelayHat.GetRelayState(2);
        // sequentRelayHat.SetRelayState(3, false);
        // relay3 = sequentRelayHat.GetRelayState(3);
        // sequentRelayHat.SetRelayState(4, false);
        // relay4 = sequentRelayHat.GetRelayState(4);

        System.Threading.Thread.Sleep(1000);


    }
}

