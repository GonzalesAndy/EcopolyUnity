# Ecopoly

Ecopoly is a Unity board game inspired by Monopoly, designed for 3 to 5 players with online multiplayer support.

Core twist: every action can increase Carbon Emissions Points (CEP). If global CEP reaches the configured threshold, everyone loses.

## Members
- GONZALES Andy
- AVERHAUS Finja
- GRYKO Urszula
- LABORY Louis
- LAVAL Jason
- VAUVERT Lukas 
- WYBOUW Esteban 
- ZMUDZINSKA Anna


## Tech Stack

- Unity 6.4
- C#
- Unity Netcode for GameObjects
- Unity Gaming Services (Lobby + Relay)

## Quick Start

1. Open the project in Unity 6.4.
2. Open scene `Assets/Scenes/NewUI/GameBoardEnv.unity`.
3. Press Play.

## Game Concept

Players move around a 40-tile board, buy properties, pay rent, draw cards, and develop districts.

Ecopoly adds environmental pressure through CEP:

- Personal CEP increases from purchases, turn emissions, some cards, and disasters.
- Personal CEP can decrease through ecological district buildings.
- Global CEP is the sum of all active players.
- If global CEP reaches the threshold defined in `GameSettings`, the game ends in collective defeat.

## Important Folders

- `Assets/Scripts/Core`: turn, board, and game authority logic
- `Assets/Scripts/Player`: player state and movement systems
- `Assets/Scripts/Cards`: chance/event/disaster behavior
- `Assets/Scripts/AI`: bot behavior and decision logic
- `Assets/Scripts/UI`: HUD, lobby, and game UI
- `Assets/ScriptableObjects`: data assets used by systems
- `Assets/Scenes/NewUI`: gameplay and flow scenes
