using CSCore.DSP;

namespace Resonance;

public class FFTStreamSettings(int binSize, int samplingRate, 
                                FftSize fftWidth, float dbNoiseFloor, 
                                float autoLevelSpeed, float smoothing,
                                float graphGain, bool normalized,
                                bool shouldAutoLevel, bool quantized)
{
    public readonly int BinSize = binSize;
    public readonly int SamplingRate = samplingRate;
    public readonly FftSize FftWidth = fftWidth;
    public int FftWidthCount => (int)FftWidth;
    public float DbNoiseFloor = dbNoiseFloor;
    public float Smoothing = smoothing;
    public float GraphGain = graphGain;
    public float AutoGain_Speed = autoLevelSpeed;
    public bool AutoGain_Enabled = shouldAutoLevel;
    public bool Normalized = normalized;
    public bool Quantized = quantized;
}
