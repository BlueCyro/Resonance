using FrooxEngine;
using Elements.Assets;
using CSCore.DSP;
using Elements.Core;

namespace Resonance;

public class FFTStreamHandler
{
    public static readonly float[] BAND_RANGES = new float[8] { 20, 60, 250, 500, 2000, 4000, 6000, 20000 };
    public static Dictionary<UserAudioStream<StereoSample>, FFTStreamHandler> FFTDict = new();
    public UserAudioStream<StereoSample> UserStream { get; }
    public int FftBinSize { get; }
    public FftSize FftWidth => fftProvider.FftSize;
    private readonly ValueStream<float>[] binStreams;
    private readonly ValueStream<float>[] bandStreams;
    private readonly FftProvider fftProvider;
    private readonly float[] fftData;
    private readonly int sampleRate;
    private readonly float correctionFactor;
    public FFTStreamHandler(UserAudioStream<StereoSample> stream, int binSize = 256, FftSize fftWidth = FftSize.Fft2048, int samplingRate = 48000)
    {
        UserStream = stream ?? throw new NullReferenceException("FFTStreamHandler REQUIRES a UserAudioStream instance");
        FftBinSize = binSize;
        fftProvider = new FftProvider(2, fftWidth)
        {
            WindowFunction = WindowFunctions.Hamming
        };
        fftData = new float[(int)fftWidth];
        binStreams = new ValueStream<float>[binSize];
        bandStreams = new ValueStream<float>[BAND_RANGES.Length - 1];
        sampleRate = samplingRate;
        correctionFactor = (float)Math.Sqrt((int)fftWidth / 2);
    }

    private static void SetStreamParams(ValueStream<float> stream)
    {
        stream.SetInterpolation();
        stream.SetUpdatePeriod(0, 0);
        stream.Encoding = ValueEncoding.Quantized;
        stream.FullFrameBits = 12; // Really, you're not gonna need more than 12 for fancy visuals :V
        stream.FullFrameMin = 0f;
        stream.FullFrameMax = 1f;
    }

    public void SetupStreams()
    {
        User localUser = UserStream.LocalUser;

        var space = UserStream.Slot.FindSpace(null) ?? UserStream.Slot.AttachComponent<DynamicVariableSpace>();
        Slot spaceSlot = space.Slot;

        Slot variableSlot = space.Slot.AddSlot("<color=hero.green>Fft variable drivers</color>", false);

        for (int i = 0; i < FftBinSize; i++)
        {
            binStreams[i] = localUser.GetStreamOrAdd<ValueStream<float>>($"{UserStream.ReferenceID}.{i}", SetStreamParams);
            variableSlot.CreateReferenceVariable<IValue<float>>($"fft_stream_bin_{i}", binStreams[i], false);
        }

        for (int i = 0; i < bandStreams.Length; i++)
        {
            bandStreams[i] = localUser.GetStreamOrAdd<ValueStream<float>>($"{UserStream.ReferenceID}.{i}.band", SetStreamParams);
            variableSlot.CreateReferenceVariable<IValue<float>>($"fft_stream_band_{i}", bandStreams[i], false);
        }
        variableSlot.CreateVariable<int>("fft_stream_width", (int)FftWidth, false);
        variableSlot.CreateVariable<int>("fft_bin_size", FftBinSize, false);
        variableSlot.CreateVariable<bool>("fft_data_normalized", Resonance.Config!.GetValue(Resonance.Normalize), false);
    }
    public void NormalizeData()
    {
        /* This is a somewhat interesting normalization that looks weird, but I kinda wanna keep as a goodie.
        float max = float.MinValue;
        float min = float.MaxValue;

        for (int i = 0; i < array.Length; i++)
        {
            array[i] = (float)Math.Log10(1 + array[i]);
            if (array[i] > max) max = array[i];
            if (array[i] < min) min = array[i];
        }

        for (int i = 0; i < array.Length; i++)
        {
            array[i] = (array[i] - min) / (max - min);
        }
        */
    }

    public void UpdateFFTData(Span<StereoSample> samples)
    {
        bool shouldNormalize = Resonance.Config!.GetValue(Resonance.Normalize);
        float noiseFloor = Resonance.Config!.GetValue(Resonance.noiseFloor);
        float staticGain = Resonance.Config!.GetValue(Resonance.logGain);

        foreach (var sample in samples)
        {
            fftProvider.Add(sample.left, sample.right);
        }

        if (fftProvider.IsNewDataAvailable)
        {
            fftProvider.GetFftData(fftData);
            
            for (int i = 0; i < FftBinSize; i++)
            {
                // TODO: Clean up this mess later.
                float db = 10 * MathX.Log10(fftData[i] * fftData[i]);
                float normalized = MathX.Clamp((db + noiseFloor) / noiseFloor, 0f, 1f);
                float freq = i * sampleRate / ((int)FftWidth / 2);
                float gain = 1f + 2f * (float)Math.Log(i);
                float powGain = 1 + (float)Math.Pow(i / (float)FftWidth, 3f);

                float oldGain = 1f + staticGain * (float)Math.Log10(freq + 1f);

                float binValue = shouldNormalize ? normalized * normalized * oldGain : fftData[i] * fftData[i] * oldGain;

                binStreams[i].Value = MathX.LerpUnclamped(binValue, binStreams[i].Value, MathX.Clamp(Resonance.Config!.GetValue(Resonance.Smoothing), 0f , 1f));
                binStreams[i].ForceUpdate();
            }

            int samplesAdded = 0;
            int band = 0;
            float average = 0f;
            for (int i = 0; i < (int)FftWidth / 2; i++)
            {
                float currentFrequency = i * sampleRate / (int)FftWidth / 2;
                if (currentFrequency >= BAND_RANGES[band]) 
                {
                    bandStreams[band].Value = average / samplesAdded;
                    bandStreams[band].ForceUpdate();
                    band++;
                    average = 0f;
                    samplesAdded = 0;
                }
                samplesAdded++;
                average += fftData[i] * fftData[i];
            }
        }
    }

    public void DestroyStreams()
    {
        foreach (var stream in binStreams)
        {
            stream.Destroy();
        }
        foreach (var stream in bandStreams)
        {
            stream.Destroy();
        }
    }
}