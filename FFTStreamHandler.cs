using FrooxEngine;
using Elements.Assets;
using CSCore.DSP;
using Elements.Core;

namespace Resonance;

public partial class FFTStreamHandler
{
    public void Setup()
    {
        FFTDict.Add(stream, this); // Associate the handler with a user audio stream
        
        variableSlot = workingSpace.Slot.FindChildOrAdd("<color=hero.green>Fft variable drivers</color>", false);
        
        SetupBins();
        SetupBands();

        variableSlot.CreateVariable(WIDTH_VARIABLE, (int)FftWidth, false);
        variableSlot.CreateVariable(BINSIZE_VARIABLE, FftVisualSize, false);
        variableSlot.CreateVariable(NORMALIZED_VARIABLE, _normalized, false);
    }

    public void UpdateFFTData(Span<StereoSample> samples)
    {
        if (fftProvider == null)
            return;

        foreach (var sample in samples)
        {
            fftProvider.Add(sample[0], sample[1]);
        }

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

                    float normalizedDb = // Normalize the decibels between zero and one assuming a static noise floor (usually 60db for most consumer music at full volume)
                        MathX.Clamp
                            ((db + NoiseFloor) / NoiseFloor,
                            0f, 
                            1f);

                    // Further narrow down the peaks by squaring the normalized value, then apply a logarithmic gain to equalize
                    // the contribution of higher frequencies across the spectrum. This helps the graph look pretty and well-balanced.
                    float binValue = _normalized ?
                        normalizedDb * normalizedDb * gainLookup[i] : 
                        fftData[i] * fftData[i];
                    // *Do note, however, that this means the data is absolutely USELESS for any analytical purposes. Any visuals made
                    // for these values should mostly be intensity-based and not anything complex like beat detection or what have you.

                    
                    // Lerp between the current bin value and the last to produce smoothing.
                    // This is actually how the volume meter component does it's smoothing.
                    float smoothed = lastFftData[i] =
                        MathX.LerpUnclamped
                            (binValue,
                            lastFftData[i], 
                            MathX.Clamp(SmoothSpeed, 0f, 1f));

                    // If the smoothed value is greater than one, set the autoLevel to a factor
                    // that will correct it back down to one. Doing this for each bin will normalize
                    // the graph back down to a 0 -> 1 range and keep it from clipping.
                    if (smoothed > 1f)
                        autoLevelFactor = Math.Min(1f / smoothed, autoLevelFactor);

                    binStreams[i].Value = smoothed * Gain;
                    binStreams[i].ForceUpdate(); // Force update the stream to push the value

                }

                // Average & populate the 7 frequency bands
                if (bandIndex < BAND_RANGES.Length && 
                    freqLookup[i] >= BAND_RANGES[bandIndex] &&
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
            
            autoLevelFactor = MathX.LerpUnclamped(autoLevelFactor, 1f, AutoLevelSpeed); // Slowly return autolevel back to one

        }
    }

    private void SetStreamParams(ValueStream<float> stream)
    {
        stream.SetInterpolation();
        stream.SetUpdatePeriod(0, 0);
        stream.Encoding = Quantized ? ValueEncoding.Quantized : ValueEncoding.Full;
        stream.FullFrameBits = 12; // Really, you're not gonna need more than 12 for fancy visuals :V
        stream.FullFrameMin = 0f;
        stream.FullFrameMax = 1f;
    }

    private void SetBandStreamParams(ValueStream<float> stream)
    {
        stream.SetInterpolation();
        stream.SetUpdatePeriod(0, 0);
        stream.Encoding = ValueEncoding.Full; // Realistically, only 7 full-depth floats won't kill anybody
        stream.FullFrameMin = 0f;
        stream.FullFrameMax = 1f;
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
        for (int i = 0; i < (int)FftWidth; i++)
        {
            Resonance.Msg($"Bin: {i} Log: {gainLookup[i]} Freq: {freqLookup[i]}");
        }
    }
}