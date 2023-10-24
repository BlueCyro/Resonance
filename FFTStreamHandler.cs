using FrooxEngine;
using Elements.Assets;
using CSCore.DSP;
using Elements.Core;

namespace Resonance;

public class FFTStreamHandler
{
    public static Dictionary<UserAudioStream<StereoSample>, FFTStreamHandler> FFTDict = new();
    public UserAudioStream<StereoSample> UserStream { get; }
    public bool Normalized
    {
        get => Resonance.Normalize_Fft;
        set
        {
            workingSpace?.TryWriteValue(NORMALIZED_VARIABLE, value);
        }
    }
    public int FftVisualSize => binStreams.Length;
    public FftSize FftWidth => fftProvider.FftSize;
    public bool Quantized 
    { 
        get => binStreams.Any(s => s.Encoding == ValueEncoding.Quantized);
        set
        {
            for (int i = 0; i < binStreams.Length; i++) 
                binStreams[i].Encoding = value ? ValueEncoding.Quantized : ValueEncoding.Full; 
        }
    }
    public ValueStream<float>[] binStreams;
    public ValueStream<float>[] bandStreams;
    private readonly FftProvider fftProvider;
    private readonly float[] fftData;
    private readonly float[] lastFftData;
    private readonly float[] gainLookup;
    private readonly float[] freqLookup;
    private readonly int sampleRate;
    private float autoLevel = 1f;
    private Slot? variableSlot;
    private readonly DynamicVariableSpace? workingSpace;
    private const string WIDTH_VARIABLE = "fft_stream_width";
    private const string BINSIZE_VARIABLE = "fft_bin_size";
    private const string NORMALIZED_VARIABLE = "fft_data_normalized";
    public static readonly float[] BAND_RANGES = { 20, 60, 250, 500, 2000, 4000, 6000, 20000 };
    public FFTStreamHandler(UserAudioStream<StereoSample> stream, int binSize = 256, FftSize fftWidth = FftSize.Fft2048, int samplingRate = 48000)
    {
        UserStream = stream ?? throw new NullReferenceException("FFTStreamHandler REQUIRES a UserAudioStream instance!");

        fftProvider = new FftProvider(2, fftWidth)
        {
            WindowFunction = WindowFunctions.Hamming
        };

        fftData = new float[(int)fftWidth];
        lastFftData = new float[(int)fftWidth];
        binStreams = new ValueStream<float>[binSize];
        bandStreams = new ValueStream<float>[BAND_RANGES.Length - 1];
        sampleRate = samplingRate;
        gainLookup = new float[(int)fftWidth];
        freqLookup = new float[(int)fftWidth];

        // Generate logarithmic gain correction & frequency as a lookup so we don't need to calculate it all the time
        for (int i = 0; i < (int)fftWidth; i++)
        {
            freqLookup[i] = (float)i * sampleRate / ((int)FftWidth / 2);
            gainLookup[i] = (float)Math.Log10(freqLookup[i] + 1f);
        }

        FFTDict.Add(stream, this); // Associate the handler with a user audio stream

        workingSpace = UserStream.Slot.FindSpace(null) ?? UserStream.Slot.AttachComponent<DynamicVariableSpace>();
        variableSlot = workingSpace.Slot.FindChildOrAdd("<color=hero.green>Fft variable drivers</color>", false);
    }

    private static void SetStreamParams(ValueStream<float> stream)
    {
        stream.SetInterpolation();
        stream.SetUpdatePeriod(0, 0);
        stream.Encoding = Resonance.Full_BitDepth_Bins ? ValueEncoding.Full : ValueEncoding.Quantized;
        stream.FullFrameBits = 12; // Really, you're not gonna need more than 12 for fancy visuals :V
        stream.FullFrameMin = 0f;
        stream.FullFrameMax = 1f;
    }

    private static void SetBandStreamParams(ValueStream<float> stream)
    {
        stream.SetInterpolation();
        stream.SetUpdatePeriod(0, 0);
        stream.Encoding = ValueEncoding.Full; // Realistically, only 7 full-depth floats won't kill anybody
        stream.FullFrameMin = 0f;
        stream.FullFrameMax = 1f;
    }

    public void SetupStreams()
    {
        var space = UserStream.Slot.FindSpace(null) ?? UserStream.Slot.AttachComponent<DynamicVariableSpace>();
        variableSlot = space.Slot.FindChildOrAdd("<color=hero.green>Fft variable drivers</color>", false);
        
        SetupBins();

        SetupBands();

        variableSlot.CreateVariable(WIDTH_VARIABLE, (int)FftWidth, false);
        variableSlot.CreateVariable(BINSIZE_VARIABLE, FftVisualSize, false);
        variableSlot.CreateVariable(NORMALIZED_VARIABLE, Resonance.Normalize_Fft, false);
    }
    
    private void SetupBins()
    {
        User localUser = UserStream.LocalUser;
        
        for (int i = 0; i < FftVisualSize; i++)
        {
            string varName = $"fft_stream_bin_{i}";
            binStreams[i] = localUser.GetStreamOrAdd<ValueStream<float>>($"{UserStream.ReferenceID}.bin.{i}", SetStreamParams);
            if (workingSpace?.TryReadValue(varName, out IValue<float> stream) ?? false && stream != null)
            {
                workingSpace.TryWriteValue(varName, binStreams[i]);
                continue;
            }
            variableSlot?.CreateReferenceVariable<IValue<float>>(varName, binStreams[i], false);
        }
    }

    private void SetupBands()
    {
        User localUser = UserStream.LocalUser;
        for (int i = 0; i < bandStreams.Length; i++) // Allocating full-bit-depth floats for the band streams since they're unmodified and the energies can be quite small
        {
            string varName = $"fft_stream_band_{i}";
            bandStreams[i] = localUser.GetStreamOrAdd<ValueStream<float>>($"{UserStream.ReferenceID}.band.{i}", SetBandStreamParams);
            if (workingSpace?.TryReadValue(varName, out IValue<float> stream) ?? false && stream != null)
            {
                workingSpace.TryWriteValue(varName, binStreams[i]);
                continue;
            }
            variableSlot?.CreateReferenceVariable<IValue<float>>(varName, bandStreams[i], false);
        }
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
        if (fftProvider == null)
            return;

        foreach (var sample in samples)
        {
            fftProvider.Add(sample[0], sample[1]);
        }

        float gain = Resonance.AutoLevel ? autoLevel : Resonance.Gain; // If autolevelling is enabled, use rolling autolevel, otherwise use static gain
        autoLevel = MathX.LerpUnclamped(autoLevel, 1f, Resonance.AutoLevelSpeed); // Slowly return autolevel back to one

        if (fftProvider.IsNewDataAvailable)
        {
            fftProvider.GetFftData(fftData);
            
            int samplesAdded = 0;
            int bandIndex = 0;
            float average = 0f;

            for (int i = 0; i < (int)FftWidth / 2; i++)
            {
                // Populate bin streams
                if (i < FftVisualSize && binStreams[i] != null && !binStreams[i].IsDestroyed)
                {
                    float db = 10 * MathX.Log10(fftData[i] * fftData[i]); // Compute the decibels for the FFT magnitude

                    float normalized = // Normalize the decibels between zero and one assuming a static noise floor (usually 60db for most consumer music at full volume)
                        MathX.Clamp
                            ((db + Resonance.NoiseFloor) / Resonance.NoiseFloor, 
                            0f, 
                            1f);

                    // Further narrow down the peaks by squaring the normalized value, then apply a logarithmic gain to equalize
                    // the contribution of higher frequencies across the spectrum. This helps the graph look pretty and well-balanced.
                    float binValue = Resonance.Normalize_Fft ?
                        normalized * normalized * gainLookup[i] : 
                        fftData[i] * fftData[i];

                    // Lerp between the current bin value and the last to produce smoothing.
                    // This is actually how the volume meter component does it's smoothing.
                    float smoothed = lastFftData[i] =
                        MathX.LerpUnclamped
                            (binValue,
                            lastFftData[i], 
                            MathX.Clamp(Resonance.Smoothing, 0f, 1f));

                    // If the smoothed value is greater than one, set the autoLevel to a factor
                    // that will correct it back down to one. Doing this for each bin will normalize
                    // the graph back down to a 0 -> 1 range and keep it from clipping.
                    if (smoothed > 1f)
                        autoLevel = Math.Min(1f / smoothed, autoLevel);
                    
                    binStreams[i].Value = smoothed * gain;
                    binStreams[i].ForceUpdate(); // Force update the stream to push the value
                }

                // Average & populate the 7 frequency bands
                if (freqLookup[i] >= 
                    BAND_RANGES[bandIndex] && 
                    bandStreams[bandIndex] != null && 
                    !bandStreams[bandIndex].IsDestroyed)
                {
                    bandStreams[bandIndex].Value = average / samplesAdded;
                    bandStreams[bandIndex].ForceUpdate();
                    bandIndex++;
                    average = 0f;
                    samplesAdded = 0;
                }
                samplesAdded++;
                average += fftData[i] * fftData[i];
            }
        }
    }

    private void DestroyBins()
    {
        foreach (var stream in binStreams)
        {
            stream.Destroy();
        }
    }
    private void DestroyBands()
    {
        foreach (var stream in bandStreams)
        {
            stream.Destroy();
        }
    }

    private void DestroyStreams()
    {
        DestroyBins();
        DestroyBands();
    }

    // Destroy it all >:)
    public void Destroy()
    {
        FFTDict.Remove(UserStream);
        variableSlot?.Destroy();
        DestroyStreams();
    }

    public static void Destroy(UserAudioStream<StereoSample>? stream)
    {
        if (stream != null && FFTDict.TryGetValue(stream, out FFTStreamHandler handler))
        {
            handler.Destroy();
        }
    }

    public static void Destroy(IChangeable c)
    {
        Destroy(c as UserAudioStream<StereoSample>);
    }

    public void PrintDebugInfo()
    {
        Resonance.Msg($"PRINT RESONANCE DEBUG:");
        Resonance.Msg($"{FftWidth}");
        Resonance.Msg($"{sampleRate}");
        Resonance.Msg("Log lookup values:");
        for (int i = 0; i < FftVisualSize; i++)
        {
            Resonance.Msg($"Log: {gainLookup[i]} Freq: {freqLookup[i]}");
        }
    }
}