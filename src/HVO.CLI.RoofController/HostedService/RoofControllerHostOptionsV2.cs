namespace HVO.CLI.RoofController.HostedService;

public record class RoofControllerHostOptionsV2
{
 public int RestartOnFailureWaitTime { get; set; } = 10;    

}

