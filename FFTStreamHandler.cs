using FrooxEngine;
using Elements.Assets;
using Elements.Core;

namespace Resonance;

public partial class FFTStreamHandler
{
    public void Setup()
    {
        FFTDict.Add(UserStream, this); // Associate the handler with a user audio stream

        WorkingSpace = UserStream.Slot.FindSpace(null) ?? stream.Slot.AttachComponent<DynamicVariableSpace>();
        VariableSlot = WorkingSpace.Slot.FindChildOrAdd("<color=hero.green>Fft variable drivers</color>", false);

        SetupBins();
        SetupBands();

        VariableSlot.CreateVariable(WIDTH_VARIABLE, Settings.FftWidthCount, false);
        VariableSlot.CreateVariable(BINSIZE_VARIABLE, Settings.BinSize, false);
        VariableSlot.CreateVariable(NORMALIZED_VARIABLE, Settings.Normalized, false);

        // Generate logarithmic gain correction & frequency as a lookup so we don't need to calculate it all the time
        freqLookup = Enumerable.Range(0, Settings.FftWidthCount)
                    .Select(i => (float)i * settings.SamplingRate / Settings.FftWidthCount)
                    .ToArray();

        gainLookup = freqLookup
                    .Select(freq => (float)Math.Log10(freq + 1f))
                    .ToArray();

        Initialized = true;
    }

    public void UpdateFFTData(Span<StereoSample> samples)
    {
        if (!Initialized)
            throw new FFTStreamNotInitializedException();

        foreach (var sample in samples)
            fftProvider.Add(sample[0], sample[1]);


        if (fftProvider.IsNewDataAvailable)
        {
            fftProvider.GetFftData(fftData);

            int samplesAdded = 0;
            int bandIndex = 0;
            float average = 0f;

            for (int i = 0; i < Settings.FftWidthCount / 2; i++)
            {
                // Populate bin streams
                if (i < Settings.BinSize && binStreams[i] != null && !binStreams[i].IsDestroyed)
                {
                    // Compute the decibels for the FFT magnitude
                    float db = 10 * MathX.Log10(fftData[i] * fftData[i]);

                    // Normalize the decibels between zero and one assuming a static noise floor (usually 60db for most consumer music at full volume)
                    float normalizedDb =
                        MathX.Clamp
                            ((db + Settings.DbNoiseFloor) / Settings.DbNoiseFloor,
                            0f,
                            1f);

                    // Further narrow down the peaks by squaring the normalized value, then apply a logarithmic gain to equalize
                    // the contribution of higher frequencies across the spectrum. This helps the graph look pretty and well-balanced.
                    float binValue = Settings.Normalized ?
                        normalizedDb * normalizedDb * gainLookup![i] :
                        fftData[i] * fftData[i];


                    // *Do note, however, that this means the data is absolutely USELESS for any analytical purposes. Any visuals made
                    // for these values should mostly be intensity-based and not anything complex like beat detection or what have you.


                    // Lerp between the current bin value and the last to produce smoothing.
                    // This is actually how the volume meter component does it's smoothing.
                    float smoothed = lastFftData[i] =
                        MathX.LerpUnclamped
                            (binValue,
                            lastFftData[i],
                            MathX.Clamp(Settings.Smoothing, 0f, 1f));

                    // If the smoothed value is greater than one, set the autoLevel to a factor
                    // that will correct it back down to one. Doing this for each bin will normalize
                    // the graph back down to a 0 -> 1 range and keep it from clipping.
                    if (smoothed > 1f)
                        autoGainFactor = Math.Min(1f / smoothed, autoGainFactor);

                    binStreams[i].Value = smoothed * AutoGain;
                    binStreams[i].ForceUpdate(); // Force update the stream to push the value

                }

                // Average & populate the 7 frequency bands
                if (bandIndex < BAND_RANGES.Length &&
                    freqLookup![i] >= BAND_RANGES[bandIndex] &&
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

            // Slowly return autolevel back to one
            autoGainFactor = MathX.LerpUnclamped(autoGainFactor, 1f, Settings.AutoGain_Speed);

        }
    }

    private void SetStreamParams(ValueStream<float> stream, string name)
    {
        stream.Name = name;
        stream.SetInterpolation();
        stream.SetUpdatePeriod(0, 0);
        stream.Encoding = Settings.Quantized ? ValueEncoding.Quantized : ValueEncoding.Full;
        stream.FullFrameBits = 12; // Really, you're not gonna need more than 12 for fancy visuals :V
        stream.FullFrameMin = 0f;
        stream.FullFrameMax = 1f;
    }

    private void SetBandStreamParams(ValueStream<float> stream, string name)
    {
        stream.Name = name;
        stream.SetInterpolation();
        stream.SetUpdatePeriod(0, 0);
        stream.Encoding = ValueEncoding.Full; // Realistically, only 7 full-depth floats won't kill anybody
        stream.FullFrameMin = 0f;
        stream.FullFrameMax = 1f;
    }

    private void SetupBins()
    {
        User localUser = UserStream.LocalUser;

        for (int i = 0; i < Settings.BinSize; i++)
        {
            string varName = $"fft_stream_bin_{i}";
            var binName = $"{UserStream.ReferenceID}.bin.{i}";
            binStreams[i] = localUser.GetStreamOrAdd<ValueStream<float>>(binName, (s) => SetStreamParams(s, binName));
            if (WorkingSpace?.TryReadValue(varName, out IValue<float> stream) ?? false && stream != null)
            {
                WorkingSpace.TryWriteValue(varName, binStreams[i]);
                continue;
            }
            VariableSlot?.CreateReferenceVariable<IValue<float>>(varName, binStreams[i], false);
        }
    }

    private void SetupBands()
    {
        User localUser = UserStream.LocalUser;
        for (int i = 0; i < bandStreams.Length; i++) // Allocating full-bit-depth floats for the band streams since they're unmodified and the energies can be quite small
        {
            string varName = $"fft_stream_band_{i}";
            var bandName = $"{UserStream.ReferenceID}.band.{i}";
            bandStreams[i] = localUser.GetStreamOrAdd<ValueStream<float>>(bandName, (s) => SetBandStreamParams(s, bandName));
            if (WorkingSpace?.TryReadValue(varName, out IValue<float> stream) ?? false && stream != null)
            {
                WorkingSpace.TryWriteValue(varName, bandStreams[i]);
                continue;
            }
            VariableSlot?.CreateReferenceVariable<IValue<float>>(varName, bandStreams[i], false);
        }
    }

    public void SetQuantized(bool state)
    {
        if (!Initialized)
            throw new FFTStreamNotInitializedException();

        foreach (var stream in binStreams)
        {
            stream.Encoding = Settings.Quantized ? ValueEncoding.Quantized : ValueEncoding.Full;
        }
        Settings.Quantized = state;
    }

    public void SetNormalized(bool state)
    {
        if (!Initialized)
            throw new FFTStreamNotInitializedException();

        WorkingSpace?.TryWriteValue(NORMALIZED_VARIABLE, state);
        Settings.Normalized = state;
    }

    private void DestroyStreams()
    {
        DestroyBins();
        DestroyBands();
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

    // Destroy it all >:)
    public void Destroy()
    {
        FFTDict.Remove(UserStream);
        VariableSlot?.Destroy();
        DestroyStreams();
    }

    public static void Destroy(UserAudioStream<StereoSample>? stream)
    {
        if (stream != null && FFTDict.TryGetValue(stream, out FFTStreamHandler handler))
        {
            handler.Destroy();
        }
    }

    public static void Destroy(IDestroyable c)
    {
        Destroy(c as UserAudioStream<StereoSample>);
    }

    public void PrintDebugInfo()
    {
        if (!Initialized)
            throw new FFTStreamNotInitializedException();

        Resonance.Msg($"PRINT RESONANCE DEBUG:");
        Resonance.Msg($"{Settings.FftWidthCount}");
        Resonance.Msg($"{Settings.SamplingRate}");
        Resonance.Msg("Log lookup values:");
        for (int i = 0; i < Settings.FftWidthCount; i++)
        {
            Resonance.Msg($"Bin: {i} Log: {gainLookup![i]} Freq: {freqLookup![i]}");
        }
    }
}

[Serializable]
public class FFTStreamNotInitializedException : Exception
{
    public FFTStreamNotInitializedException() : base("Stream settings handler is not initialized! Did you run Setup() after creating the FFTStreamHandler?") { }
    public FFTStreamNotInitializedException(string message) : base(message) { }
    public FFTStreamNotInitializedException(string message, Exception inner) : base(message, inner) { }
    protected FFTStreamNotInitializedException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}