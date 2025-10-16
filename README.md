# SSMP <img src="res/round_icon.svg" width="52" align="right">

## What is Silksong Multiplayer?
As the name might suggest, Silksong Multiplayer (SSMP) is a multiplayer mod for the popular 2D action-adventure game Hollow Knight: Silksong.
The main purpose of this mod is to allow people to host games and let others join them in their adventures.
There is a dedicated [Discord server](https://discord.gg/KbgxvDyzHP) for the mod where you can ask questions or generally talk about the mod.
Moreover, you can leave suggestions or bug reports. The latest announcements will be posted there.

## Install
### Thunderstore
When using an installer that is compatible with Thunderstore, everything should work auto-magically.

### Manual install
SSMP works with the [BepInExPack for Silksong](https://thunderstore.io/c/hollow-knight-silksong/p/BepInEx/BepInExPack_Silksong/) 
found on the [Silksong Thunderstore community](https://thunderstore.io/c/hollow-knight-silksong).
Follow the instructions for the BepInExPack on Thunderstore and once completed, do the following:
- Make a new directory named `SSMP` in the BepInEx plugins folder found: `path\to\Hollow Knight Silksong\BepInEx\plugins\`
- Unzip the `SSMP.zip` file from the [releases](https://github.com/Extremelyd1/SSMP/releases) into this new directory
  - Make sure that all `.dll` files are in the `SSMP` directory without any additional directories

## Usage
The mod can be accessed by using the "Start Multiplayer" option in the main menu of the game.
Once in the multiplayer menu, there is an option to host a game on the entered port and an option to join a game at the entered address and entered port.
Playing multiplayer with people on your LAN is straightforward, but playing over the internet requires some extra work.
Namely, the port of the hosted game should be forwarded in your router to point to the device you are hosting on.
Alternatively, you could use software to facilitate extending your LAN, such as [Hamachi](https://vpn.net).

If you start hosting or joining a server, the mod will prompt you to select a save file to use.
This save file is only used locally and will not synchronise with the server.

The mod features a chat window that allows users to enter commands.
The chat input can be opened with a key-bind (`Y` by default), which feature the following commands:
- `addon <enable|disable|list> [addon(s)]`: Enable, disable, or list addons.
- `list`: List the names of the currently connected players.
- `set <setting name> [value]`: Read or write a setting with the given name and given value. For a list of possible
  settings, see the section below.
- `skin <skin ID>`: Change the currently used skin ID for the player.
- `team <None|Moss|Hive|Grimm|Lifeblood>`: Change the team that the player is on.
- `announce <message>`: Broadcast a chat message to all connected players.
- `kick <auth key|username|ip address>`: Kick the player with the given authentication key, username or IP address.
- `ban <auth key|username>`: Ban the player with the given authentication key or username. If given a username, will only
  issue the ban if a user with the given username is currently connected to the server.
- `unban <auth key>`: Unban the player with the given authentication key.
- `banip <auth key|username|ip address>`: Ban the IP of the player with the given authentication key, username or IP address.
  If given an auth key or a username, will only issue the ban if a user with the given auth key or username is currently
  connected to the server.
- `unbanip <ip address>`: Unban the IP of the player with the given IP address.

### Authentication/authorization
Each user will locally generate an auth key for authentication and authorization.
This key can be used to whitelist and authorize specific users to allow them to join
the server or execute commands that require higher permission.

- `whitelist [args]`: Manage the whitelist with following options:
    - `whitelist <on|off>`: Enable/disable the whitelist.
    - `whitelist <add|remove> [name|auth key]`: Add/remove the given username or auth key to/from
      the whitelist. If given a username that does not correspond with an online player, the username will be
      added to the 'pre-list'. Then, if a new player with a username on this list will login, they are automatically
      whitelisted.
    - `whitelist <clear> [prelist]`: Clear the whitelist (or the pre-list if `prelist` was given as argument).
- `auth [name|auth key]`: Authorize the online player with the given username or auth key.
- `deauth [name|auth key]`: De-authorize the online player with the given username or auth key.

### Standalone server
The standalone server is currently not finished.

### Settings
There are a lot of configurable settings that can change how the mod functions.

The values below can be read and modified by the `set` command described above.
All names for the settings are case-insensitive, but are written in case for clarity.
- `IsPvpEnabled`: whether player vs. player damage is enabled.
    - Aliases: `pvp`
- `AlwaysShowMapIcons`: whether player's map locations are always shared on the in-game map.
    - Aliases: `globalmapicons`
- `OnlyBroadcastMapIconWithWaywardCompass`: whether a player's map location is only shared when they have the Wayward Compass charm equipped.
  Note that if map locations are always shared, this setting has no effect.
    - Aliases: `compassicon`, `compassicons`, `waywardicon`, `waywardicons`
- `DisplayNames`: Whether overhead names should be displayed.
    - Aliases: `names`
- `TeamsEnabled`: Whether player teams are enabled.
  Players on the same team cannot damage each other.
  Teams can be selected from the client settings menu.
    - Aliases: `teams`
- `AllowSkins`: Whether player skins are allowed.
  If disabled, players will not be able to use a skin locally, nor will it be transmitted to other players.
    - Aliases: `skins`

### Skins
The system for skins is currently not implemented entirely.
While it is possible to change skin IDs using the command system, it will most likely not work correctly.

## Contributing
There are a few ways you can contribute to this project, which are all outlined below.
Please also read and adhere to the [contributing guide](https://github.com/Extremelyd1/SSMP/blob/master/CONTRIBUTING.md).

### Github issues
If you have any suggestions or bug reports, please leave them at the [issues page](https://github.com/Extremelyd1/SSMP/issues).
Make sure to label the issues correctly and provide a proper explanation.
Suggestions or feature requests can be labeled with "Enhancement", bug reports with "Bug", etc.

## Patreon
If you like this project and are interested in its development, consider becoming a supporter on
[Patreon](https://www.patreon.com/Extremelyd1). You will get access to development posts, sneak peeks
and early access to new features. Additionally, you'll receive a role in the Discord server with access
to exclusive channels.

## Copyright and license
HKMP is a game modification for Hollow Knight that adds multiplayer.  
Copyright (C) 2025  Extremelyd1

    This library is free software; you can redistribute it and/or
    modify it under the terms of the GNU Lesser General Public
    License as published by the Free Software Foundation; either
    version 2.1 of the License, or (at your option) any later version.

    This library is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
    Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public
    License along with this library; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301
    USA
