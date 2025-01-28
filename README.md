(Work in progress)

# Asio Audio for Unity
A simple package that simulates Unity Audio Sources that send audio data through ASIO protocol, and sends Audio Sources positions using OSC protocol.
The developement was made using Unity 2022.3.19f1.

This package uses different frameworks:

- [UnityOSC](https://t-o-f.info/UnityOSC/) by thomasfredericks, to send Audio Sources positions through OSC protocol.
- [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) by GlitchEnzo, to install the following NuGet package:
	- [NAudio](https://github.com/naudio/NAudio) by Mark Heath, an open-source .NET audio library. 

## Intended Workflow
The purpose of this package is to simulate the behaviour of Unity Audio Sources, with 2 main goals:
- Adding the ability to send real-time position of Audio Sources through OSC protocol.
- Modifying the standard Unity audio output, and using the ASIO driver type to send audio data instead.

The idea behind is to use [REAPER](https://www.reaper.fm/) and [Spat Revolution](https://www.flux.audio/project/spat-revolution/) softwares to simulate spatialisation of Audio Sources on any output configuration.

REAPER will be used to get audio data using the ReaRoute ASIO driver, then to send the data to Spat Revolution.

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

<img src="/docs/reapertracks.png" alt="drawing" width="400"/>

Now that we configured REAPER ASIO inputs with 4 channels, we will now configure the output to Spat Revolution. For this we will need the Spat Revolution Send VST plugin:

- Open Flux Center and install Spat Revolution Send. This will install a VST plugin on your PC.

- Now on REAPER, go to Options > Preferences, search for VST section.

- On VST plug-in paths check that the path `%COMMONPROGRAMFILES%/VS3` is configured, click Re-scan... > Clear cache and re-scan VST paths for all plugins, it should add the plugin to REAPER.

- Check if the VST plugin is available. To do this, select a track, click on the FX button (NOT THE IN FX), and search for `VST3: Spat Revolution - Send (FLUX) (64ch)`:

<img src="/docs/reaperfx.png" alt="drawing" width="400"/>

- On the Spat Revolution Send VST window, enable Local Audio Path to send audio data to Spat:

<img src="/docs/reaperenable.png" alt="drawing" width="400"/>

- Do the 2 last steps on each track created (from "Check if the VST plugin is available").

### Spat Revolution

Open Flux Center and download Spat Revolution, the installation should be done automatically.

- Launch Spat Revolution and go directly to the Preferences tab. 

- **For audio data:** Go to IO Hardware, set the output device, and configure the sample rate and the block size so it matches the available properties of the device. The two properties must also match the properties set in REAPER about the Dummy Audio device, to avoid any unwanted audio artifact.

<img src="/docs/spatio.png" alt="drawing" width="400"/> <img src="/docs/reaperdummy.png" alt="drawing" width="400"/>

-  **For audio source position:** Go to OSC Main, and be sure that OSC is enabled by ticking the first button. Then, on OSC Connections, add a new connection: `input | Spat Revolution - Plugins`, and set up the IP address to localhost (127.0.0.1), and the port 8100.

<img src="/docs/spatosc.png" alt="drawing" width="400"/> <img src="/docs/spatoscip.png" alt="drawing" width="400"/>

- Once done, go to the Setup tab, and you should already see 4 inputs on the Input line, corresponding to the 4 tracks set on REAPER.
	>If you don't see them, refer to the [REAPER](#reaper) section to set them up.

- Do the following:
	- Add 4 sources on the Source line, connect them to the 4 tracks and convert the sources to mono.
	- Add a room on the Room line and specify the output configuration wanted.
	- Add a master transcoder on the Master Transcoder line, and set the output to the same config as the room.
	- Finally, add an output on the Output line.
- Connect them as follows:

<img src="/docs/spatconfig.png" alt="drawing" width="800"/>

You can see on the Room tab the final configuration of the room and sources.

### Unity

Once REAPER and Spat Revolution are set, we will open the package on a Unity project. To do this:

- Create a project or open an existing project using Unity Hub. A 2022.3 version should work fine.

- After opening the project, go to Edit > Project Settings... On Player > Other Settings section, make sure the API Compatibility Level is set to `.NET Framework`.

<img src="/docs/unitysettings.png" alt="drawing" width="800"/>

- Download and import the Unity Package to the project (see Releases). Once done, open the Example Scene on `Assets/AsioAudioUnity/Example/AsioAudioScene.unity`.

- Identify the ASIO Audio Sources on the Scene, and pick one. On the Inspector tab, select the component `Custom ASIO Audio Source` and tick `Play On Awake`.

- Click Play, and see if audio data is transmitted to REAPER. The REAPER track that should get data is the one identified with the `Target Output Channel` on the `Custom ASIO Audio Source` component, which will point to the corresponding ReaRoute input. 
	> Example: On Unity, if a `Custom ASIO Audio Source` has its `Play On Awake` property ticked, and its `Target Output Channel` property set to 3, the REAPER track identified by input ReaRoute 3 should get the data.

<img src="/docs/finalresult.png" alt="drawing" width="800"/>

## Documentation
Here's a short documentation about the Asio Audio