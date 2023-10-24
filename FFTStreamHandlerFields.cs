using FrooxEngine;
using Elements.Assets;
using CSCore.DSP;
using Elements.Core;

namespace Resonance;

public partial class FFTStreamHandler(UserAudioStream<StereoSample> stream,
                                int binSize = 256,
                                FftSize fftWidth = FftSize.Fft2048,
                                int samplingRate = 48000,
                                bool streamNormalized = true,
                                float dbNoiseFloor = 60f,
                                float autoLevelSpeed = 0.001f,
                                bool shouldAutoLevel = true,
                                float graphGain = 1f,
                                float smoothing = 0.35f,
                                bool quantized = true)
{
    public static Dictionary<UserAudioStream<StereoSample>, FFTStreamHandler> FFTDict = new();
    public readonly UserAudioStream<StereoSample> UserStream = stream ?? throw new NullReferenceException("FFTStreamHandler REQUIRES a UserAudioStream instance!");

    // Stream properties & values
    public bool Normalized
    {
        get => _normalized;
        set
        {
            _normalized = value;
            workingSpace?.TryWriteValue(NORMALIZED_VARIABLE, value);
        }
    }
    public bool Quantized 
    { 
        get => _quantized;
        set
        {
            for (int i = 0; i < binStreams.Length; i++) 
                binStreams[i].Encoding = value ? ValueEncoding.Quantized : ValueEncoding.Full;

            _quantized = value;
        }
    }
    private bool _quantized = quantized;
    public int FftVisualSize => MathX.Clamp(binStreams.Length, 1, (int)fftWidth);
    public FftSize FftWidth => fftProvider.FftSize;

    // Behavioral variables
    private bool _normalized = streamNormalized;
    public bool AutoLevel = shouldAutoLevel;
    public float Gain // If autolevelling is enabled, use rolling autolevel, otherwise use static gain
    {
        get => AutoLevel ? autoLevelFactor : staticGain;
        set => staticGain = value;
    }
    public float AutoLevelSpeed = autoLevelSpeed;
    public float NoiseFloor = dbNoiseFloor;
    public float SmoothSpeed = smoothing;
    public readonly int sampleRate = samplingRate;
    private float autoLevelFactor = 1f;
    private float staticGain = graphGain;

    // FFT Data & handling
    private readonly float[] fftData = new float[(int)fftWidth];
    private readonly float[] lastFftData = new float[(int)fftWidth];
    public ValueStream<float>[] binStreams = new ValueStream<float>[binSize];
    public ValueStream<float>[] bandStreams = new ValueStream<float>[BAND_RANGES.Length];
    
    // Generate logarithmic gain correction & frequency as a lookup so we don't need to calculate it all the time
    private readonly float[] gainLookup = Enumerable.Range(0, (int)fftWidth)
                                            .Select(i => (float)i * samplingRate / ((int)fftWidth))
                                            .Select(freq => (float)Math.Log10(freq + 1f))
                                            .ToArray();
                                            
    private readonly float[] freqLookup = Enumerable.Range(0, (int)fftWidth)
                                            .Select(i => (float)i * samplingRate / ((int)fftWidth))
                                            .ToArray();

    private readonly FftProvider fftProvider = new(2, fftWidth) { WindowFunction = WindowFunctions.Hamming };

    // World stuff
    private Slot? variableSlot;
    private readonly DynamicVariableSpace workingSpace = stream.Slot.FindSpace(null) ?? stream.Slot.AttachComponent<DynamicVariableSpace>();

    // Constants
    private const string WIDTH_VARIABLE = "fft_stream_width";
    private const string BINSIZE_VARIABLE = "fft_bin_size";
    private const string NORMALIZED_VARIABLE = "fft_data_normalized";
    public static readonly float[] BAND_RANGES = { 20, 60, 250, 500, 2000, 4000, 6000, 20000 };
}