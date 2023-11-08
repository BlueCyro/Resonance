using ResoniteModLoader;
using FrooxEngine;
using Elements.Core;

namespace Resonance;

public partial class Resonance : ResoniteMod
{
    public const int DEFAULT_FFTSIZE = 2048;

    [Range(0f, 0.95f)]
    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<float> smoothing = 
        new(
            "FFT Smoothing",
            "Controls how smoothly the FFT appears to change", 
            () => 0.35f
        );
    
    public static float Smoothing => Config!.GetValue(smoothing);

    [Range(0f, 96f, "0db")]
    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<float> noisefloor = 
        new(
            "Noise Floor",
            "Determines the noise floor for the input signal (60db is low enough for most music)",
            () => 60f
        );
    
    public static float NoiseFloor => Config!.GetValue(noisefloor);

    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<float> gain =
        new(
            "Gain",
            "Applies a static gain to the FFT signal - useful if the FFT is clipping (does nothing if Auto Gain is enabled)",
            () => 0.5f
        );
    
    public static float Gain => Config!.GetValue(gain);

    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> autogain =
        new(
            "Auto Gain",
            "When enabled: Automatically manages the gain of the FFT output to always avoid clipping",
            () => true
        );
    
    public static bool AutoGain => Config!.GetValue(autogain);

    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> hiresfft =
        new(
            "High-Resolution FFT",
            "Changes the FFT bin width from 2048 to 4096 - useful for expanding detail in the lower ranges (requires stream respawn)",
            () => false
        );

    public static bool HiResFft => Config!.GetValue(hiresfft); 
    public static int ConfigFftWidth => HiResFft ? High_Resolution_Fft_Override : DEFAULT_FFTSIZE;

    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> lowlatencyaudio =
        new(
            "Low-Latency Audio",
            "If other people see the FFT desync often, turn this on to reduce audio stream latency (May degrade stream audio quality!!)",
            () => false
        );

    public static bool LowLatencyAudio => Config!.GetValue(lowlatencyaudio);

    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<int> visiblebins =
        new(
            "Visible bins",
            "How many FFT bins are accessible via dynamic variables (don't change unless instructed or are building a visualizer, requires stream respawn)",
            () => 256,
            true
        );
    
    
    public static int VisibleBins => Config!.GetValue(visiblebins);

    // Advanced keys

    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<float> autogainspeed =
        new(
            "Auto Gain Speed",
            "Factor of smoothing to apply to auto gaining - lower is slower",
            () => 0.001f,
            true
        );
    
    public static float AutoGainSpeed => Config!.GetValue(autogainspeed); 

    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> QUANTIZE_BINS =
        new(
            "Quantize bins",
            "Turning this off will stream all FFT values at full bit-depth (VERY BAD FOR NETWORK, ONLY ENABLE IF YOU ABSOLUTELY NEED TO!!!!)",
            () => true,
            true
        );
    
    public static bool Quantize_Bins => Config!.GetValue(QUANTIZE_BINS);  

    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<int> HIGH_RESOLUTION_FFT_OVERRIDE =
        new(
            "Hi-Res FFT override",
            "Will override the 'High-Resolution FFT' setting (if enabled) and change the FFT width to whatever this setting is instead (requires stream respawn)",
            () => 4096,
            true
        );
    
    public static int High_Resolution_Fft_Override => (int)Math.Pow(2, Math.Ceiling(Math.Log(Config!.GetValue(HIGH_RESOLUTION_FFT_OVERRIDE), 2))); // Ensure a power of 2

    [AutoRegisterConfigKey]
    public static readonly ModConfigurationKey<bool> NORMALIZE_FFT =
        new(
            "FFT Normalization",
            "Enables FFT normalization (false gives you the raw FFT energies, leave true if you want pretty visuals)",
            () => true,
            true
        );
    
    public static bool Normalize_Fft => Config!.GetValue(NORMALIZE_FFT);

    // Changed events
    private static void NORMALIZE_FFT_changed(object? obj)
    {
        foreach (var handler in FFTStreamHandler.FFTDict.Values)
            handler.SetNormalized((bool)obj!);
    }

    private static void QUANTIZE_BINS_changed(object? obj)
    {
        foreach (var handler in FFTStreamHandler.FFTDict.Values)
            handler.SetQuantized((bool)obj!);
    }

    private static void Lowlatencyaudio_changed(object? obj)
    {
        bool state = (bool)obj!;

        foreach (var stream in FFTStreamHandler.FFTDict.Keys)
        {
            var audioStream = stream.Stream.Target;
            if (audioStream != null)
            {
                audioStream.MinimumBufferDelay.Value = state ? 0.05f : 0.2f;
                audioStream.BufferSize.Value = state ? 12000 : 24000;
            }
        }
    }
    
    private static void Autogainspeed_changed(object? obj)
    {
        foreach (var handler in FFTStreamHandler.FFTDict.Values)
        {
            handler.Settings.AutoGain_Speed = (float)obj!;
        }
    }

    private static void Autogain_changed(object? obj)
    {
        foreach (var handler in FFTStreamHandler.FFTDict.Values)
        {
            handler.Settings.AutoGain_Enabled = (bool)obj!;
        }
    }

    private static void Gain_changed(object? obj)
    {
        foreach (var handler in FFTStreamHandler.FFTDict.Values)
        {
            handler.Settings.GraphGain = (float)obj!;
        }
    }

    public static void Noisefloor_changed(object? obj)
    {
        foreach (var handler in FFTStreamHandler.FFTDict.Values)
        {
            handler.Settings.DbNoiseFloor = (float)obj!;
        }
    }

    private static void Smoothing_changed(object? obj)
    {
        foreach (var handler in FFTStreamHandler.FFTDict.Values)
        {
            handler.Settings.Smoothing = (float)obj!;
        }
    }

    public static void HandleEvents()
    {
        autogain.OnChanged += o => RunInAllWorldsSynchronously(() => Autogain_changed(o));
        smoothing.OnChanged += o => RunInAllWorldsSynchronously(() => Smoothing_changed(o));
        noisefloor.OnChanged += o => RunInAllWorldsSynchronously(() => Noisefloor_changed(o));
        gain.OnChanged += o => RunInAllWorldsSynchronously(() => Gain_changed(o));
        autogainspeed.OnChanged += o => RunInAllWorldsSynchronously(() => Autogainspeed_changed(o));
        lowlatencyaudio.OnChanged += o => RunInAllWorldsSynchronously(() => Lowlatencyaudio_changed(o));
        QUANTIZE_BINS.OnChanged += o => RunInAllWorldsSynchronously(() => QUANTIZE_BINS_changed(o));
        NORMALIZE_FFT.OnChanged += o => RunInAllWorldsSynchronously(() => NORMALIZE_FFT_changed(o));
    }

    public static void RunInAllWorldsSynchronously(Action act)
    {
        foreach(World w in Engine.Current.WorldManager.Worlds)
        {
            w.RunSynchronously(act);
        }
    }
}

