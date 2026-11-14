# BadWolf Voice Alarm

A Windows 11 desktop app that uses a Discord bot account to listen for voice-state events and starts a **local, repeating alarm** when a user enters a monitored voice channel. The bot **does not join voice channels**, capture audio, or read messages.

## Requirements

- Windows 10/11
- .NET 8 SDK + Visual Studio 2022 with the **.NET desktop development** workload (for source builds)
- A Discord server where you can add a bot
- Internet access while the app runs

## 1. Create and install the Discord bot

1. Open https://discord.com/developers/applications and create a new application.
2. Go to **Bot**, create a bot if needed, and copy/reset the **Bot Token**. Treat it like a password.
3. Go to **Installation** or **OAuth2 > URL Generator**. Select the `bot` scope, choose **Guild Install**, and give the bot **View Channels** permission. The bot does not require Administrator, Connect, Send Messages, or Message Content Intent.
4. Use the generated install link to add the bot to your Discord server.
5. The gateway intent `GUILD_VOICE_STATES` is **not privileged**; no privileged-intent toggles are required.

## 2. Configure the Windows application

1. Open `BadWolfVoiceAlarm/BadWolfVoiceAlarm.csproj` in Visual Studio 2022.
2. Restore NuGet packages, build, and run.
3. Paste the bot token into **Bot token**.
4. Optional: fill **Server ID** to listen to one server only.
5. Optional: fill comma-separated **Voice-channel IDs** to listen to specific channels only.
6. Choose **Audio output device**: **System default (Windows)**, speakers, headphones, HDMI, or another active playback device. Use **Refresh devices** after plugging or unplugging audio hardware.
7. Enable **Temporarily set selected device volume to 100% and unmute** if you want the alarm to be loud. Only the chosen playback endpoint's master volume is changed, not every Windows audio device.
8. Click **Test alarm** and verify that the sound comes from the selected output.
9. Click **Connect bot**. Wait for **Connected** status, then have someone join a voice channel.

Enable Discord **User Settings > Advanced > Developer Mode**, right-click a server or voice channel, and choose **Copy ID** to find its ID.

## Behavior

- Alerts on `not in voice` -> `in voice` events.
- When **Also trigger when someone moves between voice channels** is enabled (default), alerts on channel transfers too.
- Ignores bot accounts by default.
- Does not alert for mute/unmute, deafen, streaming, or other voice-state changes within the same channel.
- If another person joins while the alarm is already active, the alarm keeps playing and the displayed event updates.
- The alarm loops until **STOP ALARM**, **Escape**, or **Stop Alarm** in the Windows notification-area menu.
- The alarm uses the selected Windows output endpoint (including for **Test alarm**). **System default (Windows)** resolves to the current default playback device when a new alarm starts.
- The selected endpoint ID is persisted in settings. If it becomes disconnected, it appears as **(unavailable)**; playback does not silently switch to a different output. Reconnect it or select another device and save.
- If the maximum-volume option is enabled, the previous **master volume and mute state** of that same playback device are restored when the alarm stops. Device selection changes take effect on the next alarm.
- The app continues running in the system tray when closed/minimized; use **Exit** in the tray menu to quit.
- Optional **Launch with Windows** uses the current user's HKCU Run entry. The registered path is the executable path you run, so enable this **after publishing to a stable folder**.
- Optional **Connect automatically** lets the app reconnect when Windows launches it.
- The bot's token is saved in `%LOCALAPPDATA%\BadWolfVoiceAlarm\settings.json`, encrypted with Windows DPAPI for the current Windows account. No plaintext token belongs in source control.

## Publish a portable single-file Windows EXE

Run from the directory containing the `.csproj` (Windows Terminal / Developer PowerShell):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

Run `publish\BadWolfVoiceAlarm.exe`. The output may include additional files depending on SDK/package behavior; retain the publish directory. To auto-start at sign-in, move the published app to its permanent location first, then enable **Launch with Windows**.

## Security and limitations

- Never paste your bot token in chat, commit it to GitHub, or share the settings file.
- The monitoring app **must be running and connected** to Discord to receive alerts.
- A sleeping, hibernating, powered-off, or logged-out Windows PC will not be awakened by this application.
- Discord requires that the bot has been installed on the target server; a personal Discord user token is **not** supported.
- Changing a selected endpoint's master volume affects other audio playing through that endpoint while the alarm is active. Test at a comfortable level before using 100% volume.
- Alarms are local, not audio transmitted into Discord.

## Technology

C# / WPF / .NET 8, `Discord.Net.WebSocket` 3.20.2, `NAudio` 2.4.0, Windows DPAPI.
