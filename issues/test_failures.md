### Issue: Two Tests Failing in HVO.Iot.Devices.Tests.GpioButtonWithLedTests

#### Failing Tests:
1. **ButtonPress_WithDebouncing_IgnoresRapidChanges**  
   - **Description:** The debounce logic may not be filtering rapid events correctly. Review the event firing mechanism to ensure only one ButtonDown event fires within the debounce interval.

2. **Dispose_AfterCreation_DisposesCleanly**  
   - **Description:** After disposal, events are still firing, suggesting that cleanup is incomplete. Ensure that Dispose detaches all event handlers and stops timers/threads.

#### Additional Issue:
- An MSBuild error is occurring regarding a missing solution file in the working directory during post-job cleanup. 
  - **Logs:** [MSBuild Error Logs](https://github.com/RoySalisbury/HVOv9/actions/runs/16821065141/job/47647895829#step:6:47)

#### Related Test Code:
- [GpioButtonWithLedTests.cs](https://github.com/RoySalisbury/HVOv9/blob/af723181609658860e2b009e1b4d5214d137a558/src/HVO.Iot.Devices.Tests/GpioButtonWithLedTests.cs)