# Realmbox
Exalt Account Manager is a simple program written in C# utilizing the Windows Presentation Foundation GUI Framework to open the RotMG Exalt.exe without having to use the launcher and also being able to open multiple instances of the game with ease.

## Installation and Setup
- Unzip the Realmbox.zip from one of the releases.
- Run Realmbox.exe
- Enter the Exalt Path by clicking on the textbox and clicking save.
- Enter account info

Settings are saved at `%APPDATA%\Realmbox\settings.json`

## Limitations
Only works for Windows, but works for steam accounts now.

## Steam Accounts
Instead of entering the e-mail, `steamworks:<steamid>` and the password that you can get by using [Fiddler](https://www.telerik.com/fiddler) to look at the `https://www.realmofthemadgod.com/account/verify` HTTP request that is made via the Steam Launcher to get an access token. Look at the body and copy the password.

## Support and Contributions
You can request a new feature by submitting an issue to the GitHub Repository. If you would like to implement a new feature, go ahead and fork the repository, create a new branch and then create a PR.

Before you submit an issue, please search the issue tracker, maybe an issue for your problem already exists and the discussion might inform you of workarounds readily available.

## Bypassing "token for different machine" error
It's very rare to get this kind of error and it's still unknown why that happens, but there is a workaround implemented that will bypass this problem. 
Instead of using the generated device token from your computer, you can set the device/clientToken manually in the settings tab (3rd tab) within the application.
You want to set the clientToken that gets sent when you would open the game normally via the official launcher.

You will have to use [Fiddler](https://www.telerik.com/fiddler) to do this. You want to look for the POST request that gets sent to https://www.realmofthemadgod.com/account/verify. Check the body and look for "clientToken" copy and paste that into the textbox and save the settings.
If you can't see the request you might have to change fiddler settings to capture and decrypt HTTPS traffic. 
Fiddler -> Options -> HTTPS Tab -> Make sure that Decrypt HTTPS traffic is selected. 

Alternatively you can use a [tool](https://github.com/MaikEight/EAM-GetClientHWID) that was made by [MaikEight](https://github.com/MaikEight) for this kind of purpose.

## Plans
Once .NET MAUI is released this will probably see a redesign with some fancy UI.
