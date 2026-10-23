# Nael Dive Caller

Dalamud plugin for The Unending Coil of Bahamut (Ultimate). During Nael, it reads where the five dragons spawn and prints the divebomb bait spots in a local chat message only you can see.

```
Dragons (clockwise from N): N, S | SW | W, NW
CURSED pattern - don't use the default waymarks blindly
Dive 1: E (moved off the usual waymark)
Dive 2: B (SSE)
Dive 3: C (SW)
```

## Installing

1. In game, open `/xlsettings` → **Experimental**.
2. Under **Custom Plugin Repositories**, paste this URL, tick **Enabled** and save:
   ```
   https://raw.githubusercontent.com/Juandibp/NaelDiveCaller/main/repo.json
   ```
3. Open `/xlplugins`, search for **Nael Dive Caller** and install it.

## How it picks spots

Dragons are counted clockwise from north and split 2-1-2: the first divebomb takes dragons 1-2, the second dragon 3, the third dragons 4-5. Each dive is baited on its usual waymark (A, B, C, read from the waymarks placed in game) unless that would send a dive line through the middle of the arena. In that case the bait moves to the spot on the edge that keeps the dives furthest from the middle.

## Commands

- `/naeldive` recompute for the current dragons.
- `/naeldive test N S SW W NW` try a dragon layout anywhere (compass names or degrees).

## Building

Needs the .NET 10 SDK and a Dalamud dev install (XIVLauncher).

```
dotnet build NaelDiveCaller -c Release
```

Add `NaelDiveCaller/bin/Release/NaelDiveCaller.dll` under `/xlsettings` → Experimental → Dev Plugin Locations.

To release, bump `<Version>` in `NaelDiveCaller/NaelDiveCaller.csproj` and push a matching tag (`git tag v0.1.1.0 && git push origin v0.1.1.0`). The release workflow builds the plugin, uploads `latest.zip` and updates `repo.json`.

`SolverCheck` is a small console app that runs the solver without the game:

```
dotnet run --project SolverCheck -- N,S,SW,W,NW
```
