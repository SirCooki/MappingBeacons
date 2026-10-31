# Mapping Beacons

A Valheim mod (BepInEx + Jötunn) that adds scouting beacons: build one, feed it fuel, and it maps the island it
stands on, ring by ring, out to the coast. Open the beacon to copy what it has mapped onto your own map.
In game the mod is called **Scouting Beacons**.

## Beacons

| Beacon | Cost | Optimal range | Speed | Fuel ratio | Markers |
|---|---|---|---|---|---|
| Huginn's Cairn | 10 Fine wood, 10 Bronze | 1 km² | 48 in-game hours per km² | 1 | none |
| Muninn's Spire | 15 Stone, 15 Silver | 2 km² | 18 in-game hours per km² | 0.9 | locations |
| Allfather's Eye | 20 Yggdrasil wood, 20 Black metal | 4 km² | 6 in-game hours per km² | 0.75 | locations and ores |

- Built with the hammer (Misc), no crafting station needed. The model is a recoloured Obliterator; wisps circle it
  while it works (2 / 4 / 8) and turn red and pulse when it works past its optimal range.
- **Mapping:** outward from the beacon in 12 m rings, over land connected to it. The ocean is the limit: one beacon
  maps one island and never the sea around it. Rings the builder had already explored when placing it are free.
- **Fuel:** the beacon has a chest-like inventory. "Add all fuel" moves fuel from your inventory into it, "Feed beacon"
  turns it into fuel. Wood 1, Core wood 1.2, Ancient bark 1.4, Fine wood 1.6, Yggdrasil wood / Ashwood 1.8, Coal 2.5,
  Surtling core 50, Sap 25, Black core 100, and more (configurable).
- **Cost per ring** rises evenly to one "stack" (50 fuel) at the optimal range, then by one stack more for every ring
  past it, times the tier's fuel ratio.
- **Status panel:** fuel (plus what's waiting in the inventory), theoretical fuel need, exploring range, fuel range,
  fuel depletion countdown, and warnings for limit overreach and an island that is fully explored.

## Screenshots

**Huginn's Cairn** (bronze)

![Huginn's Cairn at work](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/bronze_action.jpg)
![Huginn's Cairn window](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/bronze_menu.jpg)

**Muninn's Spire** (silver)

![Muninn's Spire at work](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/silver_action.jpg)
![Muninn's Spire window](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/silver_menu.jpg)
![Map before](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/silver_pre.jpg)
![Map after, with locations](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/silver_result.jpg)

**Allfather's Eye** (black metal)

![Allfather's Eye at work](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/blackmetal_action.jpg)
![Allfather's Eye window](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/blackmetal_menu.jpg)
![Map after, with locations and ores](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/blackmetal_result.jpg)

## Markers

- **Locations** (silver and black metal): burial chambers, troll and frost caves, sunken crypts, villages, infested
  mines, charred fortresses, Morgen holes, fire holes, drake nests and more. Optional: boss altars, each trader
  (Haldor, Hildir, Bog Witch, Altar of Potential: the placed one, or every possible spot until it's placed), forts,
  Ashlands fortress ruins.
- **Ores** (black metal): copper (Cu), scrap iron (Fe), silver (Ag), flametal and brains; tin (Tn) and obsidian
  optional. Ore in areas nobody has visited doesn't exist yet in Valheim, so the mod predicts it from the world seed
  by replaying the game's own placement; areas that exist use the real deposits.

## Other mods

"Block automatic feeding" (on by default) keeps chest-automation mods away from beacons: ServersideQoL AutoStore
doesn't store items into them, ChestButler doesn't sort into them, and OttoFuel / ServersideQoL AutoProcess and
TameAssist don't take fuel out of them.

## Install

Needs BepInExPack Valheim and Jötunn. Put `ScoutingBeacons.dll` in `BepInEx/plugins/ScoutingBeacons/`.
On a server, the server and every player need the same version. All settings are server settings
(`BepInEx/config/SirCookie.ScoutingBeacons.cfg`).

## Build

`dotnet build -c Release` (.NET SDK 8) against your Valheim install (`ValheimDir` in the .csproj, Steam path by
default); the DLL is copied into the game's plugins folder.

Console command for testing (cheats on, single player or host): `sb_orecheck [radius]` compares predicted ore with
the real deposits around you.

## Licence

MIT, by SirCookie.
