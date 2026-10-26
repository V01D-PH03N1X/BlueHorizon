# Blue Horizon

Competitive tactical first-person shooter by Sebastian Schindler — Unity 6000.3.13f1, Netcode for GameObjects,
dedicated authoritative servers.

This repository contains the **vertical slice**: HeavyBomb on the graybox map *Horizon Depot* with the agent
Striker, two weapons, a suppressor, and a full round/match loop on a dedicated server.

```powershell
powershell -ExecutionPolicy Bypass -File Tools\build.ps1              # client + dedicated server -> Builds\
powershell -ExecutionPolicy Bypass -File Tools\run-local-match.ps1    # local server + 2 clients
powershell -ExecutionPolicy Bypass -File Tools\run-tests.ps1          # EditMode tests
powershell -ExecutionPolicy Bypass -File Tools\run-all-autotests.ps1  # automated multiplayer scenarios
```

Close the Unity Editor before running the tools. Full documentation: [Documentation/README.md](Documentation/README.md).
