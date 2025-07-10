namespace HVO.WebSite.RoofControllerV4.HostedServices;

public record RoofControllerHostOptions
{
 public int RestartOnFailureWaitTime { get; set; } = 10;    
}
