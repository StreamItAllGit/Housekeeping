using System;

namespace Housekeeping.Models
{
    public class RemoteControlRequest
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
        public string Action { get; set; } = ""; // "StartQueue", "StopQueue", "StartJob", "StopJob", "RestartApp"
        public string? TargetQueueId { get; set; }
        public string? TargetQueueName { get; set; }
        public string? TargetJobId { get; set; }
        public string? TargetJobName { get; set; }
        public bool IsDryRun { get; set; }
        public string SenderHost { get; set; } = Environment.MachineName;
        public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class RemoteControlResponse
    {
        public string RequestId { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
