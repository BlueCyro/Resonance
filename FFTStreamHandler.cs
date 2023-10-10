using FrooxEngine;
using Elements.Assets;
using CSCore.DSP;

namespace Resonance;

public class FFTStreamHandler
{
    public static Dictionary<UserAudioStream<StereoSample>, FFTStreamHandler> FFTDict = new();
    public UserAudioStream<StereoSample> UserStream { get; }
    public int FftBinSize { get; }
    public FftSize FftWidth 
    {
        get => fftProvider.FftSize;
    }
    private readonly ValueStream<float>[] binStreams;
    private readonly ValueStream<float>[] bandStreams;
    private readonly FftProvider fftProvider;
    private readonly float[] fftData;
    public FFTStreamHandler(UserAudioStream<StereoSample> stream, int binSize = 256, FftSize fftWidth = FftSize.Fft2048)
    {
        UserStream = stream ?? throw new NullReferenceException("FFTStreamHandler REQUIRES a UserAudioStream instance");
        FftBinSize = binSize;
        fftProvider = new FftProvider(2, fftWidth)
        {
            WindowFunction = WindowFunctions.Hanning
        };
        fftData = new float[(int)fftWidth];
        binStreams = new ValueStream<float>[binSize];
        bandStreams = new ValueStream<float>[6];
        SetupStreams();
    }
    private void SetupStreams()
    {
        User localUser = UserStream.LocalUser;

        var space = UserStream.Slot.FindSpace(null) ?? UserStream.Slot.AttachComponent<DynamicVariableSpace>();
        Slot spaceSlot = space.Slot;

        Slot variableSlot = space.Slot.AddSlot("<color=hero.green>Fft variable drivers</color>");

        for (int i = 0; i < FftBinSize; i++)
        {
            binStreams[i] = localUser.GetStreamOrAdd<ValueStream<float>>($"{UserStream.ReferenceID}.{i}", stream => {
                stream.SetInterpolation();
                stream.SetUpdatePeriod(0, 0);
                stream.Encoding = ValueEncoding.Quantized;
                stream.FullFrameBits = 12; // Really, you're not gonna need more than 12 for fancy visuals :V
                stream.FullFrameMin = 0f;
                stream.FullFrameMax = 1f;
            });
            variableSlot.CreateReferenceVariable<IValue<float>>($"fft_stream_bin_{i}", binStreams[i], false);
        }
    }

    public void UpdateFFTData(Span<StereoSample> samples)
    {
        foreach (var sample in samples)
        {
            fftProvider.Add(sample.left, sample.right);
        }
        if (fftProvider.IsNewDataAvailable)
        {
            fftProvider.GetFftData(fftData);
            for (int i = 0; i < FftBinSize; i++)
            {
                binStreams[i].Value = fftData[i];
                binStreams[i].ForceUpdate();
            }
        }
    }

    public void DestroyStreams()
    {
        foreach (var stream in binStreams)
        {
            stream.Destroy();
        }
    }
}