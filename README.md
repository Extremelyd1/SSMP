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
Once the multiplayer menu is opened, you will be greeted with a dashboard from which you can choose to play online or on a local network.

### Steam Support
SSMP fully supports Steam networking.
This means you can easily host a game and invite your friends through the Steam overlay without worrying about port forwarding or IP addresses.
- **Hosting**: To host a Steam lobby, simply select the "Steam" option in the hosting menu.
- **Inviting**: Once hosting, you can use the Steam overlay to invite friends, or use the `/invite` command in-game.
- **Joining**: Friends can join your game directly through their Friends list or by accepting your invite.

### Matchmaking Service (MMS)
The mod includes a built-in server browser powered by our Matchmaking Service.
- **Lobby Browser**: You can browse public lobbies directly from the multiplayer menu.
- **Hole Punching**: The MMS facilitates NAT hole-punching, which allows players to connect to each other directly (P2P) without needing to port forward in most cases.

### Direct Connection & LAN
For those who prefer a traditional connection, you can still connect directly via IP address.
- **Hosting**: Forward the port (default `26960`) on your router to your device.
- **LAN**: Playing on the same local network works out of the box.
- **VPN**: Tools like [Hamachi](https://vpn.net) can be used to simulate a LAN over the internet.

If you start hosting or joining a server, the mod will prompt you to select a save file to use.
This save file is only used locally and will not synchronise with the server.

### Commands
The mod features a chat window that allows users to enter commands.
The chat input can be opened with a key-bind (`Y` by default), which features the following commands:
- `/help`: Show the list of available commands.
- `/list`: List the names of the currently connected players.
- `/invite` or `/inv`: Open Steam's invite dialog to invite friends to your lobby.
- `/addon <enable|disable|list> [addon(s)]`: Enable, disable, or list client addons.
- `/set <setting name> [value]`: Read or write a setting with the given name and value.
- `/skin <skin ID>`: Change the currently used skin ID for the player.
- `/team <None|Moss|Hive|Grimm|Lifeblood>`: Change the team that the player is on.
- `/announce <message>`: Broadcast a chat message to all connected players (Admin only).
- `/kick <auth key|username|ip address>`: Kick a player (Admin only).
- `/ban <auth key|username>`: Ban a player (Admin only).
- `/unban <auth key>`: Unban a player (Admin only).
- `/banip <auth key|username|ip address>`: Ban an IP address (Admin only).
- `/unbanip <ip address>`: Unban an IP address (Admin only).
- `/copysave <from> <to>`: Copy save data from one player to another (Currently disabled).
- `/debug`: Output various debug information to the log.

### Authentication/authorization
Each user will locally generate an auth key for authentication and authorization.
This key can be used to whitelist and authorize specific users to allow them to join
the server or execute commands that require higher permission.

- `/whitelist [args]`: Manage the whitelist with following options:
    - `on|off`: Enable/disable the whitelist.
    - `add|remove [name|auth key]`: Add/remove a user.
    - `clear [prelist]`: Clear the whitelist.
- `/auth <name|auth key>`: Authorize a player.
- `/deauth <name|auth key>`: De-authorize a player.

### Standalone server
It is possible to run a standalone server on Windows, Linux and Mac.
The latest executable of the server can be found on the [releases page](https://github.com/Extremelyd1/SSMP/releases).
Make sure to download the correct version for your OS.

The standalone server can be run using .NET 9.0.
If you are only concerned with running it, then download the latest [.NET 9.0 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).

After installing the .NET runtime, the executable can be run from a terminal using `./SSMPServer [port]`.
The port argument is optional and defaults to `26960` if omitted.

The server will read/create a console settings file called `consolesettings.json`, which can be changed to alter the default startup settings of the server.
This includes which port to default to if omitted on the command line.
The other setting in this file (`fullSynchronisation`) is currently unused.

The server will also read/create a server settings file called `serversettings.json`, which can be changed to control the server settings as listed in the following section.
Alternatively, settings can be changed by running the settings command on the command line.
In addition to the commands described above, the standalone server also has the following commands:
- `exit`: Will gracefully exit the server and disconnect its users.
- `log [log level(s)]`: Adjusts which log messages are output to the console.

### Settings
There are a lot of configurable settings that can change how the mod functions.

The values below can be read and modified by the `/set` command described above.
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
