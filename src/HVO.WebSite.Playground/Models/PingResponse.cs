namespace HVO.WebSite.Playground.Models
{
    /// <summary>
    /// Response model for ping/health check operations
    /// </summary>
    public class PingResponse
    {
        /// <summary>
        /// Status message indicating the API is operational
        /// </summary>
        /// <example>Pong! API is working perfectly.</example>
        public required string Message { get; set; }

        /// <summary>
        /// API version number
        /// </summary>
        /// <example>1.0</example>
        public required string Version { get; set; }

        /// <summary>
        /// UTC timestamp when the response was generated
        /// </summary>
        /// <example>2025-07-14T00:30:00.000Z</example>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Name of the machine/server responding to the request
        /// </summary>
        /// <example>HVO-SERVER-01</example>
        public required string MachineName { get; set; }
    }
}
