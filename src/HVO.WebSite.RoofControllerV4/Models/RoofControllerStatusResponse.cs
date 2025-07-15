using HVO.WebSite.RoofControllerV4.Logic;

namespace HVO.WebSite.RoofControllerV4.Models
{
    /// <summary>
    /// Response model for roof controller status API endpoints.
    /// </summary>
    public record RoofControllerStatusResponse
    {
        /// <summary>
        /// Gets or sets the current operational status of the roof controller.
        /// </summary>
        public RoofControllerStatus Status { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the roof controller is initialized and ready for operation.
        /// </summary>
        public bool IsInitialized { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the status was retrieved.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
