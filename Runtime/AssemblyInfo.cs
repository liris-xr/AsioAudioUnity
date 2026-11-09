using System.Runtime.CompilerServices;

// The editor tools (Edit mode preview) and the tests use the audio engine's internal types.
[assembly: InternalsVisibleTo("AsioAudioUnity.Editor")]
[assembly: InternalsVisibleTo("AsioAudioUnity.Tests")]
