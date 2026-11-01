# Beacons to map your surroundings

A Valheim mod (BepInEx + Jötunn) that adds scouting beacons: build one, fuel it, and map the surroundings. You can only scout as far as the ocean biome. The beacons reach and mapping speed is capped by tier. Interact with the beacon to copy new information.

Files and settings use the internal name ScoutingBeacons (`SirCookie.ScoutingBeacons`).

## Beacons

| Beacon | Cost | Optimal range | Speed | Fuel ratio | Markers |
|---|---|---|---|---|---|
| Huginn's Cairn | 10 Fine wood, 10 Bronze | 1 km² | 48 in-game hours per km² | 1 | none |
| Muninn's Spire | 15 Stone, 15 Silver | 2 km² | 18 in-game hours per km² | 0.9 | locations |
| Allfather's Eye | 20 Yggdrasil wood, 20 Black metal | 4 km² | 6 in-game hours per km² | 0.75 | locations and ores |

- The model is a recoloured Obliterator; wisps circle a working beacon, red and pulsing wisps indicate it has passed its optimal range.
- **Mapping:** circling outwards over non-ocean biomes. The ocean is the limit: one beacon
  maps one island and never the sea around it. Rings the builder had already explored are not counted in fuel cost.
- **Fuel:** Add different fuel from inventory in the beacon. "Add all fuel" moves all possible fuel from your inventory into it. Untill you press "Feed beacon" you can take it out afterwards all fuel is permanently part of the beacon and is lost when it is moved.
- **Fuel ratio:** The higher tier beacons use fuel more efficiently. 
- **Cost ** the minimum size the game sets as explored is an ingame 12x12m square. Each ring made out of 12x12m squares slowly raises the cost untill the final ring costs 50 fuel (1 stack of wood) to explore. Any information beyond that will double the cost.
- **Status panel:** fuel (plus what's waiting in the inventory), theoretical fuel need (max. exploration within optimal range), exploring range (how for away it is exploring), fuel range (how far the fuel will get the exploration),
  fuel depletion countdown (how long the beacon will keep working), warnings for limit overreach and a message that nothing further can be explored.

## Fuel

In this overview you can see how much fuel you get from each resource.

| Resource | Fuel |
|---|---|
| Wood | 1 |
| Core wood | 1.2 |
| Ancient bark | 1.4 |
| Resin | 1.5 |
| Fine wood | 1.6 |
| Yggdrasil wood | 1.8 |
| Ashwood | 1.8 |
| Greydwarf eye | 2 |
| Coal | 2.5 |
| Charcoal resin | 3 |
| Guck | 5 |
| Tar | 8 |
| Soft tissue | 10 |
| Sulfur | 12 |
| Wisp | 15 |
| Sap | 25 |
| Refined eitr | 40 |
| Surtling core | 50 |
| Black core | 100 |

## Screenshots

**Huginn's Cairn** (bronze)

![Huginn's Cairn at work](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/bronze_action.jpg)
![Huginn's Cairn window](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/bronze_menu.jpg)
![Map before](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/bronze_pre.jpg)
![Map after](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/bronze_result.jpg)

**Muninn's Spire** (silver)

![Muninn's Spire at work](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/silver_action.jpg)
![Muninn's Spire window](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/silver_menu.jpg)
![Map before](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/silver_pre.jpg)
![Map after, with locations](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/silver_result.jpg)

**Allfather's Eye** (black metal)

![Allfather's Eye at work](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/blackmetal_action.jpg)
![Allfather's Eye window](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/blackmetal_menu.jpg)
![Map before](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/blackmetal_pre.jpg)
![Map after, with locations and ores](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/blackmetal_result.jpg)
![Mistlands, with brains and copper](https://raw.githubusercontent.com/SirCooki/MappingBeacons/main/img/blackmetal_mistlands.jpg)

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

## Credits

Icon: <a href="https://www.flaticon.com/free-icons/binoculars" title="binoculars icons">Binoculars icons created by Smashicons - Flaticon</a>

## Licence

MIT, by SirCookie.
