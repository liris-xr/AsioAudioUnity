(Work in progress)

# Asio Audio for Unity
A simple package that simulates Audio Sources that send audio data through ASIO drivers using NAudio.
The developement was made using Unity 2022.3.19f1.

This package uses different frameworks:

- [UnityOSC](https://t-o-f.info/UnityOSC/) by thomasfredericks, to send Audio Sources positions through OSC protocol.
- [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) by GlitchEnzo, to install the following NuGet package:
	- [NAudio](https://github.com/naudio/NAudio) by Mark Heath, an open-source .NET audio library. 

## Intended Workflow
The final idea of this package is to simulate the behaviour of Unity Audio Sources, with 2 main purposes:
- Adding the ability to send real-time position of Audio Sources through OSC protocol.
- Modifying the standard Unity audio output, and using the ASIO driver type to send audio data instead.

The idea behind is to use [REAPER](https://www.reaper.fm/) and [Spat Revolution](https://www.flux.audio/project/spat-revolution/) softwares to simulate Audio Sources on any output configuration.

REAPER will be used to get audio data using the ReaRoute ASIO driver and send the data to Spat Revolution.

Source positions will directly be sent using OSC to Spat Revolution.

```mermaid
graph LR
A[Unity] -- ASIO Channel 1 --> B[REAPER]
A -- ASIO Channel 2 --> B
A -- ASIO Channel 3 --> B
A -- ... --> B
B -- Local Audio Path (through VST plugin) --> C{Spat Revolution}
A -- OSC --> C
```

## Getting started

### Outside Unity
A REAPER license and a Spat Revolution license (and account) are needed for this to work.

Download and install REAPER [here](https://www.reaper.fm/download.php).
> **Warning:** When installing, you will need to add the ReaRoute ASIO driver (which is not selected by default), otherwise you will not be able to retrieve ASIO data on REAPER.

Download install Flux Center (for Spat Revolution) [here](https://www.flux.audio/download/). 

### REAPER

Launch REAPER, and set the ASIO inputs (using ReaRoute ASIO) :

- Go to Options > Preferences, search for Device section and set the Audio System to Dummy Audio.
	> We don't want any output audio device set on REAPER, because the output will be handled by Spat Revolution.

	> **Warning:** The defined sample rate has to be the same as the sample rate defined in Unity (see [Unity Section](#unity)) and in Spat Revolution (see [Spat Revolution Section](#spat-revolution)). This should be defined according to which sample rates are available on the output device.

- Set 4 empty tracks (using shortcut Ctrl+T) and arm the record clicking the ![Alt Text](/docs/reaperrecord.png) button.

- For each track, change the record input (![Alt Text](/docs/reaperinput.png)) and set to ReaRoute inputs 1 to 4, if ReaRoute inputs are not available, check your REAPER installation and verify that ReaRoute ASIO has been checked while installing REAPER.

It should look like this:

![Alt Text](/docs/reapertracks.png)

Now that we configured REAPER ASIO inputs with 4 channels, we will now configure the output to Spat Revolution. For this we will need the Spat Revolution Send VST plugin:

- Open Flux Center and install Spat Revolution Send. This will install a VST plugin on your PC.

- Now on REAPER, go to Options > Preferences, search for VST section.

- On VST plug-in paths check that the path `%COMMONPROGRAMFILES%/VS3` is configured, click Re-scan... > Clear cache and re-scan VST paths for all plugins, it should add the plugin to REAPER.

- Check if the VST plugin is available. To do this, select a track, click on the FX button (NOT THE IN FX), and search for `VST3: Spat Revolution - Send (FLUX) (64ch)`:

![Alt Text](/docs/reaperfx.png)

- On the Spat Revolution Send VST window, enable Local Audio Path to send audio data to Spat:

![Alt Text](/docs/reaperenable.png)

- Do the 2 last steps on each track created (from "Check if the VST plugin is available").

### Spat Revolution

Open Flux Center and download Spat Revolution, the installation should be done automatically.

- Launch Spat Revolution and go directly to Preferences. 

- Set the output device to the desired output configuration. Set the sample rate that it first matches available sample rates from the audio output device, then reset it in REAPER on Dummy Audio if necessary.
	>You can also check on Preferences the OSC settings if the IP Addresses matches the adresses set on Unity (see [Unity Section](#unity)).

### Unity

Once REAPER and SpatRevolution are set, we will open the package on a Unity project. To do this:

- Create a project or open an existing project using Unity Hub. A 2022.3 version should work fine.

- After opening the project, go to Edit > Project Settings... On Player > Other Settings section, make sure the API Compatibility Level is set to `.NET Framework`.

- Download and import the Unity Package to the project (see Releases). Once done, open the Example Scene on `Assets/AsioAudioUnity/Example/AsioAudioScene.unity`.

- Identify the ASIO Audio Sources on the Scene, and pick one. The Inspector tab, select the component Custom ASIO Audio Source and tick Play On Initialize.

- Click Play, and see if audio data is transmitted to REAPER. The REAPER track that should get data is the one identified with the Target Output Channel on the Custom ASIO Audio Source component, which will point to the corresponding ReaRoute input. 
	> Example: On Unity, if a Custom ASIO Audio Source has its Play On Initialize property ticked, and its Target Output Channel property set to 3, the REAPER track identified by input ReaRoute 3 should get the data.