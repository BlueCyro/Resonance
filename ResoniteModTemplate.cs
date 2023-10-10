using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Reflection;
using FrooxEngine;
using Elements.Core;

namespace Resonance;

public class ResonancePatcher : ResoniteMod
{
    public override string Name => "Resonance";
    public override string Author => "Cyro";
    public override string Version => "1.0.0";
    public override string Link => "resonite.com";

    public override void OnEngineInit()
    {
        Harmony harmony = new("net.Cyro.Resonance");
    }
}
