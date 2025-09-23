using System;
using HVO.WebSite.RoofControllerV4.Models;

namespace HVO.WebSite.RoofControllerV4.Logic;

public interface IRoofControllerServiceV4
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
        /// Gets a value indicating whether the roof is currently moving (opening or closing).
        /// This property returns true when the roof is actively in motion and not at a limit switch position.
        /// </summary>
        bool IsMoving { get; }

        /// <summary>
        /// Gets the reason for the last stop operation.
        /// </summary>
        RoofControllerStopReason LastStopReason { get; }

        /// <summary>
        /// UTC timestamp of the last status transition.
        /// </summary>
        DateTimeOffset? LastTransitionUtc { get; }

        /// <summary>
        /// Initializes the roof controller hardware and prepares it for operation.
        /// </summary>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous initialization operation. The task result contains true if initialization succeeded; otherwise, false.</returns>
        Task<Result<bool>> Initialize(CancellationToken cancellationToken);

        /// <summary>
        /// Immediately stops all roof movement operations with a specified reason.
        /// </summary>
        /// <param name="reason">The reason for stopping the operation.</param>
        /// <returns>A result indicating whether the stop operation succeeded.</returns>
        Result<RoofControllerStatus> Stop(RoofControllerStopReason reason = RoofControllerStopReason.NormalStop);

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
        
        /// <summary>
        /// Pulses the clear-fault relay to reset fault conditions on the motor controller.
        /// </summary>
        /// <param name="pulseMs">Duration to hold the clear-fault relay active.</param>
        /// <returns>A result indicating whether the clear-fault pulse completed.</returns>
        Result<bool> ClearFault(int pulseMs = 250);
 
        // DigitalInput1..4 events removed; use named alias events below

        // Public input-change events removed; the service exposes protected virtual hooks instead
 
}
