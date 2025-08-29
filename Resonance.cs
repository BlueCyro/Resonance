using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using Elements.Assets;

namespace Resonance;

public partial class Resonance : ResoniteMod
{
    public override string Name => "<color=hero.cyan>🔊</color><color=hero.purple>🎶</color> Resonance";
    public override string Author => "Cyro";
    public override string Version => typeof(Resonance).Assembly.GetName().Version.ToString();
    public override string Link => "https://github.com/RileyGuy/Resonance";
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


            __instance.RunSynchronously(() =>
            {
                int index = __instance.TargetDeviceIndex ?? -1;

                //44100;
                int sampleRate;

                // Check if we're using SDL. Either non-Windows platform or ForceSDLAudio launch argument
                // Filter out Steam Voice as a special case. Unless it's the only and Default option somehow.
                Type? audioInputType = __instance.AudioSystem.AudioInputCount > 1 ?
                        __instance.AudioSystem.AudioInputs.First(ain => ain.DeviceID != "SteamVoice").GetType() :
                        __instance.AudioSystem.DefaultAudioInput.GetType();

                if (audioInputType.Name != "SDLRecordingDevice")
                {
                    sampleRate = index > 0 ? __instance.AudioSystem.AudioInputs[index].SampleRate : __instance.AudioSystem.DefaultAudioInput.SampleRate;
                }
                else
                {
                    // We need to cast AudioInput to SDLRecordingDevice to get Format.spec.Freq and set sampleRate.
                    Resonance.Msg("AudioInput is actually SDLRecordingDevice on Linux. Casting and getting sample rate...");

                    var selectedInput = index > 0 ? __instance.AudioSystem.AudioInputs[index] : __instance.AudioSystem.DefaultAudioInput;

                    ValueTuple<SDL3.SDL.AudioSpec, int> sdlRecordingDevice_Format =
                        (ValueTuple<SDL3.SDL.AudioSpec, int>)audioInputType.GetProperty("Format").GetValue(selectedInput);

                    sampleRate = sdlRecordingDevice_Format.Item1.Freq;
                }

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
            if (world.Focus != World.WorldFocus.Focused || __instance.LocalUser.IsSilenced || (ContactsDialog.RecordingVoiceMessage && ___lastDeviceIndex == __instance.AudioSystem.DefaultAudioInputIndex))
                return;

            if (FFTStreamHandler.FFTDict.TryGetValue(__instance, out FFTStreamHandler handler))
                handler.UpdateFFTData(buffer);
        }
    }
}
