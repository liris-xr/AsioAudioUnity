# Asio Audio for Unity
A simple package that simulates Audio Sources that send audio data through ASIO drivers using NAudio.
The developement was made using Unity 2022.3.19f1.

This package uses different frameworks:

- [UnityOSC](https://t-o-f.info/UnityOSC/) by thomasfredericks, to send Audio Sources positions through OSC communication.
- [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) by GlitchEnzo, to install the following NuGet package:
	- [NAudio](https://github.com/naudio/NAudio) by Mark Heath, an open-source .NET audio library. 

## Intended Workflow
The final idea of this package is to simulate the behaviour of Unity Audio Sources, with 2 main purposes:
- Adding the ability to send real-time OSC position of Audio Sources.
- Modifying the standard Unity audio output, and using the ASIO driver type to send audio data instead.

The idea behind is to use [REAPER](https://www.reaper.fm/) and [Spat Revolution](https://www.flux.audio/project/spat-revolution/) softwares to simulate Audio Sources on any output configuration.
REAPER will be used to get audio data using the ReaRoute ASIO driver and send the data to Spat Revolution.
Source positions will directly be sent using OSC to Spat Revolution.


![alt text](/docs/workflow.jpg)


## Getting started

### Outside Unity
Get a license for REAPER and Spat Revolution.

Download REAPER [here](https://www.reaper.fm/download.php).
> **Warning:** When installing, you will need to add the ReaRoute ASIO driver (which is not selected by default), otherwise you will not be able to retrieve ASIO data on REAPER.

Download Flux Center (for Spat Revolution) [here](https://www.flux.audio/download/). 





### Unity
Start by creating a new Unity project (or open an existing one).
The project needs the NuGet package NAudio
