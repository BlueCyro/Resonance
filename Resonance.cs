using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using Elements.Assets;
using System.Reflection;

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
        HandleEvents();
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
                int index = __instance.TargetDeviceIndex ?? -1;
                int sampleRate = index > 0 ? __instance.InputInterface.AudioInputs[index].SampleRate : __instance.InputInterface.DefaultAudioInput.SampleRate;
                
                FFTStreamSettings settings =
                    new
                    (
                        VisibleBins,
                        sampleRate,
                        (CSCore.DSP.FftSize)ConfigFftWidth,
                        NoiseFloor,
                        AutoGainSpeed,
                        Smoothing,
                        Gain,
                        Normalize_Fft,
                        AutoGain,
                        Quantize_Bins
                    );
                

                FFTStreamHandler streamHandler = new(__instance, settings);
                streamHandler.Setup();
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