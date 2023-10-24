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
        Config!.OnThisConfigurationChanged += HandleChanges;

        ModConfigurationExtensions.AutoAddEvents(typeof(Resonance));
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

                FFTStreamHandler streamHandler =
                    new(__instance, VisibleBins,
                    (CSCore.DSP.FftSize)width, sampleRate,
                    Normalize_Fft, NoiseFloor,
                    AutoLevelSpeed, AutoLevel,
                    Gain, Smoothing,
                    Quantize_Bins
                    );

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

// No single-key change event handlers >:(
public static class ModConfigurationExtensions
{
    public static Dictionary<ModConfigurationKey, Action<ConfigurationChangedEvent>> ConfigKeyEvents = new();

    // This is seven kinds of awful, but keeps my config keys sane :pensive:
    public static void AutoAddEvents(Type t)
    {
        var fields = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var configKeys = fields.Where(f => f.GetCustomAttribute<AutoRegisterConfigKeyAttribute>() != null);

        foreach (var key in configKeys)
        {
            Resonance.Msg($"{key.Name}");
            if (key.GetValue(null) is ModConfigurationKey configKey)
            {
                Resonance.Msg($"{key.Name} is config key!");
                var ev = fields.FirstOrDefault(f => f.Name == $"{key.Name}_changed");
                
                if (ev != null && ev.GetValue(null) is Action<ConfigurationChangedEvent> keyAction)
                {
                    configKey.Sub(keyAction);
                }
            }
        }
    }

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