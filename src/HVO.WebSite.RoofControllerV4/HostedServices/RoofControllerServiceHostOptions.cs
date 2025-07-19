namespace HVO.WebSite.RoofControllerV4.HostedServices;

public record RoofControllerServiceHostOptions
{
 public int RestartOnFailureWaitTime { get; set; } = 10;    
}
