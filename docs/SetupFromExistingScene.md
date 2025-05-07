## Setup ASIO Audio Sources From Existing Scene

If you have an existing scene and you want to convert all the Audio Sources to Custom ASIO Audio Sources, do the following steps:
- Open your scene.
- Add a [ASIO Audio Manager](/docs/Audio%20Components/AsioAudioManager.md) object by going to *GameObject > AsioAudioUnity > Asio Audio Manager*.
- Go to *AsioAudioUnity > Convert all Audio Sources in scene to Custom ASIO Audio Sources*. This should convert all (active and inactive) Audio Sources from scene to [Custom ASIO Audio Sources](/docs/Audio%20Components/CustomAsioAudioSource.md), and automatically set the Output Channels.

<img src="/docs/pictures/existingscene1.png" alt="drawing" width="800"/>

> If you also need to transmit source position via OSC from converted ASIO Audio Sources, you can go to *AsioAudioUnity > Add Source Position OSC to all Custom ASIO Audio Sources*. 
**Warning:** Be sure that an OSC Manager script is already present in scene before doing this operation.