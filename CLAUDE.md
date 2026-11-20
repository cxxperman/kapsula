# Working rules for this repo

This is kapsula.txt™ (`Kapsula.exe`), the "island" of the dev.txt™ ecosystem: a Windows app in C#/WPF with its own interface code (it does not use kit.txt's `Ui.cs`). The launcher core.txt installs it, shows its everyday settings and hosts the local hub. Shared design and the hub protocol live in the kit.txt repo (`hub/PROTOCOL.md`).

## Ecosystem rules

1. The app works without the hub and without the other apps. The hub being absent is normal, not an error.
2. Everything the app tells the other dev.txt apps goes through the hub (`CoreHub.cs` over `HubLink`). State is always sent whole.
3. The family contract (PROTOCOL.md, «Семья»): while core.txt is online and holds the tray, the own tray icon is hidden and comes back the moment the link drops; «Не беспокоить» is one for everybody (`focus.set`); other apps' messages arrive with the command `notify`; settings with `section` are shown by core.txt, and the settings window shows one line «Настраивается в core.txt» instead.
4. Setting keys and their meaning never change. Merged rows in the settings window (`Merged.cs`) read and write exactly the old keys. core.txt scenarios send `settings.set` for `autoHide` and `walk`: keep both published and accepted.

## Shared files: copied, never edited

`src/Tokens.cs`, `src/HubLink.cs`, `src/Family.cs` and `src/CoreApp.cs` are copies from kit.txt. Do not edit them here, not even to silence a warning. If one needs a change, say so: it is made in kit.txt and copied back.

## Language level: C# 5

The app is built by the compiler that ships with Windows (`csc.exe` from .NET Framework 4). It understands C# 5 only:

- no `?.`, no `$"..."`, no `nameof`, no expression-bodied members, no `out var`, no pattern matching, no tuples;
- use `delegate { }` or plain lambdas, `string.Format` or `+`, explicit `out` variables;
- WPF is written in code, no XAML; no NuGet packages (the WebView2 libraries in `lib/webview2` are the only exception).

## Text

Interface text is in Russian, plain and short, addressed as «вы», with «ёлочки» quotes and the long dash —. Product names are lowercase with .txt: kapsula.txt, core.txt. No English in the interface. Code comments are in Russian too, like the rest of the code.

## How to verify

```
powershell -ExecutionPolicy Bypass -File build.ps1    # must print OK
powershell -ExecutionPolicy Bypass -File tests.ps1    # must end with «не пройдено: 0»
```

- Kapsula has no `--selftest`: `tests.ps1` loads `Kapsula.exe` and checks the pure logic. New logic gets a check there.
- Pictures come only from a test copy next to the real one: `Kapsula.exe --capturable --demo …` with `KAPSULA_TEST_STORE=<temp folder>` and `KAPSULA_HUB_PORT=<spare port>`. `--settingsdump <folder>` draws every settings section into PNG files with the window off-screen and exits by itself; `KAPSULA_TEST_NOCORE=1` shows the window as it looks without core.txt. Look at every picture.
- Hub behaviour is tested against the reference hub on a spare port: `node <kit.txt>/hub/hub.js <port>`. Never test against port 42080: that is the user's real core.txt.

## The user's machine

- The real Kapsula runs from `%LOCALAPPDATA%\Programs\dev.txt\Kapsula`. Never start a second real copy, never close or restart it, and never touch core.txt, Aura, Discord, Steam or Explorer. Stop only processes you started, by their PID; no `taskkill /IM`.
- Do not leave windows on the screen and do not take focus: use `--settingsdump`, `--capturable` and short test runs.
- Test runs must not touch the real settings folder (`%APPDATA%\Kapsula`), the registry or the network.
- No features, numbers or claims in README or interface text that the code does not back.
