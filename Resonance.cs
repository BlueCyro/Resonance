using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using Elements.Assets;

namespace Resonance;

public partial class Resonance : ResoniteMod
{
    public override string Name => "Resonance";
    public override string Author => "Cyro";
    public override string Version => "1.0.0";
    public override string Link => "resonite.com";
    public static ModConfiguration? Config;
    public override void OnEngineInit()
    {
        Config = GetConfiguration();
        Config!.Save(true);
        Harmony harmony = new("net.Cyro.Resonance");
        harmony.PatchAll();
        Config!.OnThisConfigurationChanged += HandleChanges;
        
        lowlatencyaudio.Sub(lowlatency_changed);
        FULL_BITDEPTH_BINS.Sub(fullbitdepth_changed);
        NORMALIZE_FFT.Sub(normalizefft_changed);
    }

    [HarmonyPatch(typeof(UserAudioStream<StereoSample>))]
    static class UserAudioStreamPatcher
    {
        [HarmonyPostfix]
        [HarmonyPatch("OnAwake")]
        public static void OnAwake_Postfix(UserAudioStream<StereoSample> __instance)
        {
            __instance.ReferenceID.ExtractIDs(out _, out byte user);

            if (__instance.LocalUser != __instance.World.GetUserByAllocationID(user))
                return;

            __instance.RunSynchronously(() => {
                int width = HiResFft ? High_Resolution_Fft_Override : 2048;

                int index = __instance.TargetDeviceIndex ?? -1;
                int sampleRate = index > 0 ? __instance.InputInterface.AudioInputs[index].SampleRate : __instance.InputInterface.DefaultAudioInput.SampleRate;

                var streamHandler = new FFTStreamHandler(__instance, VisibleBins, (CSCore.DSP.FftSize)width, sampleRate);
                streamHandler.SetupStreams();
                streamHandler.PrintDebugInfo();

                var audioStream = __instance.Stream.Target;

                if (audioStream != null && LowLatencyAudio)
                {
                    audioStream.BufferSize.Value = 12000;
                    audioStream.MinimumBufferDelay.Value = 0.05f;
                } 

                __instance.Destroyed += FFTStreamHandler.Destroy;
            });
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnNewAudioData")]
        public static void OnNewAudioData_Postfix(UserAudioStream<StereoSample> __instance, Span<StereoSample> buffer, ref int ___lastDeviceIndex)
        {
            var world = __instance.World;
            if (world.Focus != World.WorldFocus.Focused || __instance.LocalUser.IsSilenced || (ContactsDialog.RecordingVoiceMessage && ___lastDeviceIndex == __instance.InputInterface.DefaultAudioInputIndex))
                return;
            
            if (FFTStreamHandler.FFTDict.TryGetValue(__instance, out FFTStreamHandler handler))
                handler.UpdateFFTData(buffer);
        }
    }
}

public static class ModConfigurationExtensions
{
    public static Dictionary<ModConfigurationKey, Action<ConfigurationChangedEvent>> ConfigKeyEvents = new();
    public static void Sub(this ModConfigurationKey key, Action<ConfigurationChangedEvent> ev)
    {
        ConfigKeyEvents[key] = ev;
    }

    public static void Unsub(this ModConfigurationKey key, Action<ConfigurationChangedEvent> ev)
    {
        if (ConfigKeyEvents.ContainsKey(key))
        {
            ConfigKeyEvents.Remove(key);
        }
    }
}