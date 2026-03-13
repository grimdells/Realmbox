# Realmbox

## Installation and Setup
- Unzip the Realmbox.zip from one of the releases.
- Run Realmbox.exe
- Enter the Exalt Path by clicking on the textbox and clicking save.
- Enter account info

Settings are saved at `%APPDATA%\Realmbox\settings.json`

## Steam Accounts
For logging in with steam accounts, replace the email field with your GUID and the password field with your Secret.
You can do this using [Fiddler](https://www.telerik.com/fiddler), and jakcodex has a good guide found [here](https://github.com/jakcodex/muledump/wiki/Steam-Users-Setup-Guide).

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

## Credit
This is just a fork of [link3337's account manager](https://github.com/link3337/ExaltAccountManager) rebuilt for multiboxing.
