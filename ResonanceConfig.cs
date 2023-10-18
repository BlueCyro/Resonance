using ResoniteModLoader;

namespace Resonance;

public partial class Resonance : ResoniteMod
{
    [AutoRegisterConfigKey]
    public static ModConfigurationKey<float> Smoothing = new("FFT Smoothing", "Controls how smoothly the FFT appears to change", () => 0.35f);

    [AutoRegisterConfigKey]
    public static ModConfigurationKey<bool> Normalize = new("FFT Normalization", "Controls wahether the FFT is normalized or raw", () => true);

    [AutoRegisterConfigKey]
    public static ModConfigurationKey<float> NoiseFloor = new("Noise floor", "Determines the noise floor for the input signal", () => 60f);

    [AutoRegisterConfigKey]
    public static ModConfigurationKey<float> Gain = new("Gain", "Applies a static gain to the FFT signal - useful if the FFT is clipping", () => 0.5f);

    [AutoRegisterConfigKey]
    public static ModConfigurationKey<bool> AutoLevel = new("AutoLevel", "Automatically manages the gain of the FFT output to avoid clipping", () => false);

    [AutoRegisterConfigKey]
    public static ModConfigurationKey<float> AutoLevelSpeed = new("AutoLevelSpeed", "Factor of smoothing to apply to auto leveling - lower is slower", () => 0.001f);
}