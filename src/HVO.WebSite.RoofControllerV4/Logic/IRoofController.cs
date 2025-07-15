using System;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using HVO;

namespace HVO.WebSite.RoofControllerV4.Logic
{
    /// <summary>
    /// Provides operations for controlling observatory roof movement and monitoring status.
    /// </summary>
    public interface IRoofController
    {
        /// <summary>
        /// Gets a value indicating whether the roof controller has been initialized and is ready for operation.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Gets the current operational status of the roof controller.
        /// </summary>
        RoofControllerStatus Status { get; }

        /// <summary>
        /// Initializes the roof controller hardware and prepares it for operation.
        /// </summary>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous initialization operation. The task result contains true if initialization succeeded; otherwise, false.</returns>
        Task<Result<bool>> Initialize(CancellationToken cancellationToken);

        /// <summary>
        /// Immediately stops all roof movement operations.
        /// </summary>
        /// <returns>A result indicating whether the stop operation succeeded.</returns>
        Result<RoofControllerStatus> Stop();

        /// <summary>
        /// Initiates the roof opening sequence.
        /// </summary>
        /// <returns>A result containing the updated roof controller status.</returns>
        Result<RoofControllerStatus> Open();

        /// <summary>
        /// Initiates the roof closing sequence.
        /// </summary>
        /// <returns>A result containing the updated roof controller status.</returns>
        Result<RoofControllerStatus> Close();
    }
}