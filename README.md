# 🔊🎶 Resonance: See what you can't hear with FFT!

Resonance is a mod for [Resonite](https://resonite.com) via [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) that lets you visualize your audio streams with FFT! ([Fast-Fourier Transform](https://www.nti-audio.com/en/support/know-how/fast-fourier-transform-fft))


## Supplementary Information
Once installed, Resonance will automatically add some new variables to your audio streams. Those appear in the form of a new slot under newly-spawned audio streams called "Fft variable drivers", you can see it highlighted below.

<img src="image.png">

I don't recommend opening this slot in an inspector, as it has quite a few dynamic variables on it that carry the streams for each FFT bin.

Fortunately you don't need to open this slot, as the variables are easily indexable up to the maximum number of bins which are displayed (Changeable in settings, defaults to 256).
Each bin can be indexed by reading a dynamic variable of type 'IValue`1[System.Single]'