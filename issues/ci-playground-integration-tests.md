# CI: Playground integration tests failing on main

Requested by: maintainer

Summary
- The latest merge on `main` caused the CI workflow ".NET Build & Test" to fail in the Playground integration tests.
- We need to capture failing run details, analyze the cause, and fix.

Action items
1. Open the failing run at: https://github.com/RoySalisbury/HVOv9/actions/workflows/dotnet.yml (copy specific run URL)
2. Expand the `Test` step, capture failing test names, error messages, and stack traces
3. Download the TRX artifact named `test-results-<run_id>` (added by workflow)
4. Reproduce locally in Release:
   - `dotnet restore ./src/HVOv9.slnx`
   - `dotnet build ./src/HVOv9.slnx -c Release`
   - `dotnet test  ./src/HVOv9.slnx -c Release -l "trx;LogFileName=Playground.trx"`
5. Suspect areas:
   - HTTPS/dev-cert differences on ubuntu-latest vs dev container
   - Port binding / ASPNETCORE_URLS
   - TestWebApplicationFactory configuration vs Release environment

Acceptance criteria
- CI is green again on main
- Root cause documented and regression prevented
- TRX artifacts available for future failures
