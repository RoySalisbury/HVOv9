using System;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace HVO.WebSite.RoofControllerV4.Logic
{
    public interface IRoofController
    {
        bool IsInitialized { get; }

        RoofControllerStatus Status { get; }

        Task<bool> Initialize(CancellationToken cancellationToken);

        void Stop();
        void Open();
        void Close();
    }
}