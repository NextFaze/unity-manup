# Mandatory Update for Unity3D

Sometimes you have an app which talks to services in the cloud. Sometimes,
those services change, and your app no longer works. Wouldn't it be great if
the app could let the user know there's an update? That's what this module
does.

Mandatory Update (manup) works like this:

1. Your app starts up, before performing any application initialization, it
   downloads a small file from a remote server

2. The small file contains the following information
   * The current latest version of the app
   * The minimum required version
   * Whether the app is enabled

3. The app compares itself with the version metadata, and presents an alert to
   the user. Alerts come in three flavours
   * Mandatory Update required. The user will be notified that they need to
     update to continue. The alert has a link to the relevant app store.
   * Optional Update. The user will be notified there is an update, but will
     have the option to continue using the current version
   * Maintenance Mode. The user will be notified that the app is unavailable,
     and to try again later.

4. The app waits for the manup service to complete, then continues
   initialisation like normal

## Requirements

Manup assumes you are using Semantic Versioning for your app, and requires at least 2 values (i.e major.minor)

## Installation

Simply import the Unity package to get started. If you are already using SimpleJSON in your project, feel free to unselect it - otherwise there will be a conflict.

## Usage

Once the package is imported, you will find the ManUp folder under Plugins. To get started you can drag the prefab into your main startup scene, this will keep the ManUp object persistent throughout the life of the app.

* If an Event System is not detected, ManUp will create it's own.
* You will need to set the Config URL in the editor to where you will be hosting your ManUp file. To make development easier, you can override this in the editor with a local text asset and drop it into the Config Override field. Be sure to also set the platform for the platform you wish to target.
* If you wish to customise your UI layout, you can either do this by updating the prefab objects, or creating your own. Please ensure to assign the following to ManUp
  * UI Panel (Should be the parent for the UI)
  * Title and Message Text
  * Ok button and text
  * Update button and text
  * Version text - if you wish to print the current app version

### Remote file
You need a hosted json file that contains the version metadata. This _could_ be part of your API. However, 
often the reason for maintenance mode is because your API is down. An s3 bucket may be a safer bet,
even though it means a little more work in maintaining the file.

```json
{
  "ios": {
    "latest": "2.4.1",
    "minimum": "2.1.0",
    "url": "http://example.com/myAppUpdate",
    "enabled": true
  },
  "android": {
    "latest": "2.5.1",
    "minimum": "2.1.0",
    "url": "http://example.com/myAppUpdate/android",
    "enabled": true
  },
}
```

## Demonstration App
An example can be found in this repository \o/
