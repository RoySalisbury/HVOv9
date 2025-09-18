using System.Device.I2c;
using Iot.Device.OneWire;

namespace HVO.CLI.RoofController;

class Program
{
    private const byte I2C_MEM_REVISION_MAJOR_ADD = 0x00;


    static void Main(string[] args)
    {
        var settings = new I2cConnectionSettings(1, 0x0e);
        using var device = I2cDevice.Create(settings);
        //using var hat = new Sequent4RelayHat(device);


        Span<byte> readBuffer = new byte[1];
        device.WriteRead(new byte[] { (byte)SEQ_I2C_MEM.I2C_MEM_REVISION_HW_MAJOR_ADD }, readBuffer);
        var data = readBuffer.ToArray();


        Span<byte> readBuffer2 = new byte[2];
        device.WriteRead(new byte[] { (byte)SEQ_I2C_MEM.I2C_MEM_REVISION_MAJOR_ADD }, readBuffer2);
        var data2 = readBuffer2.ToArray();


    }
}

public enum SEQ_I2C_MEM : byte
{
    I2C_MEM_RELAY_VAL = 0,
    I2C_MEM_RELAY_SET,
    I2C_MEM_RELAY_CLR,
    I2C_MEM_DIG_IN,
    I2C_MEM_AC_IN,
    I2C_MEM_LED_VAL,
    I2C_MEM_LED_SET,
    I2C_MEM_LED_CLR,
    I2C_MEM_LED_MODE, //0-auto, 1 - manual;
    I2C_MEM_EDGE_ENABLE,
    I2C_MEM_ENC_ENABLE,
    I2C_MEM_SCAN_FREQ,
    // I2C_MEM_PULSE_COUNT_START = I2C_MEM_SCAN_FREQ + SCAN_FREQ_SIZE,
    // I2C_MEM_PPS = I2C_MEM_PULSE_COUNT_START + (IN_CH_NO * COUNT_SIZE),
    // I2C_MEM_ENC_COUNT_START = I2C_MEM_PPS + IN_CH_NO * IN_FREQENCY_SIZE,
    // I2C_MEM_PWM_IN_FILL = I2C_MEM_ENC_COUNT_START + (ENC_NO * ENC_COUNT_SIZE),
    // I2C_MEM_IN_FREQENCY = I2C_MEM_PWM_IN_FILL + (IN_CH_NO * PWM_IN_FILL_SIZE),
    // I2C_MEM_IN_FREQENCY_END = I2C_MEM_IN_FREQENCY + (IN_CH_NO * IN_FREQENCY_SIZE)
    //     - 1,
    // I2C_MEM_PULSE_COUNT_RESET,//2 bytes to be one modbus register
    // I2C_MEM_ENC_COUNT_RESET = I2C_MEM_PULSE_COUNT_RESET + 2,//2 bytes to be one modbus register
    // I2C_MODBUS_SETINGS_ADD = I2C_MEM_ENC_COUNT_RESET + 2,
    // I2C_NBS1,
    // I2C_MBS2,
    // I2C_MBS3,
    // I2C_MODBUS_ID_OFFSET_ADD,
    // I2C_MEM_EXTI_ENABLE,
    // I2C_MEM_BUTTON, //bit0 - state, bit1 - latch

    // // INDUSTRIAL VERSION ADDED 1
    // I2C_CRT_IN_VAL1_ADD, // current vales scaled as A/100 (1 = 0.01A) 16-bit signed integer
    // I2C_CRT_IN_RMS_VAL1_ADD = I2C_CRT_IN_VAL1_ADD + ANALOG_VAL_SIZE * IN_CH_NO, //current RMS values scaled as A/100 16bit unsigned integer
    // I2C_MEM_CALIB_VALUE = I2C_CRT_IN_RMS_VAL1_ADD + ANALOG_VAL_SIZE * IN_CH_NO, // floating point value expressing the current in A
    // I2C_MEM_CALIB_CHANNEL = I2C_MEM_CALIB_VALUE + 4,
    // I2C_MEM_CALIB_KEY, //set calib point -> 0xaa; reset calibration on the channel -> 0x55; save zero current offset -> 0x11
    // I2C_MEM_CALIB_STATUS,
    // I2C_MEM_WDT_RESET_ADD,
    // I2C_MEM_WDT_INTERVAL_SET_ADD,
    // I2C_MEM_WDT_INTERVAL_GET_ADD = I2C_MEM_WDT_INTERVAL_SET_ADD + 2,
    // I2C_MEM_WDT_INIT_INTERVAL_SET_ADD = I2C_MEM_WDT_INTERVAL_GET_ADD + 2,
    // I2C_MEM_WDT_INIT_INTERVAL_GET_ADD = I2C_MEM_WDT_INIT_INTERVAL_SET_ADD + 2,
    // I2C_MEM_WDT_RESET_COUNT_ADD = I2C_MEM_WDT_INIT_INTERVAL_GET_ADD + 2,
    // I2C_MEM_WDT_CLEAR_RESET_COUNT_ADD = I2C_MEM_WDT_RESET_COUNT_ADD + 2,
    // I2C_MEM_WDT_POWER_OFF_INTERVAL_SET_ADD,
    // I2C_MEM_WDT_POWER_OFF_INTERVAL_GET_ADD = I2C_MEM_WDT_POWER_OFF_INTERVAL_SET_ADD + 4,

    // I2C_MEM_DIAG_RASP_V = I2C_MEM_WDT_POWER_OFF_INTERVAL_GET_ADD + 4,
    // I2C_MEM_DIAG_RASP_V1,
    // I2C_MEM_DIAG_SNS_VCC,
    // I2C_MEM_DIAG_SNS_VCC1,
    // // END INDUSTRIAL VERSION ADDED 1


    I2C_MEM_REVISION_HW_MAJOR_ADD = 0x78,
    I2C_MEM_REVISION_HW_MINOR_ADD,
    I2C_MEM_REVISION_MAJOR_ADD,
    I2C_MEM_REVISION_MINOR_ADD,

    I2C_MEM_TH_RES_START_ADD,
    //I2C_MEM_TH_RES_END_ADD = I2C_MEM_TH_RES_START_ADD + ANALOG_VAL_SIZE * IN_CH_NO,
    //I2C_MEM_TH_TEMP_START_ADD = I2C_MEM_TH_RES_END_ADD,

    //SLAVE_BUFF_SIZE = 256
}


public sealed class Sequent4RelayHat : IAsyncDisposable
{
    private I2cDevice? _i2cDevice;

    public Sequent4RelayHat(I2cDevice i2cDevice)
    {
        _i2cDevice = i2cDevice ?? throw new ArgumentNullException(nameof(i2cDevice), $"I2C Device can't be null");
    }

    public ValueTask DisposeAsync()
    {
        // Dispose of unmanaged resources here if necessary
        return ValueTask.CompletedTask;
    }
}