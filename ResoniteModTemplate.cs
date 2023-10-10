using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Reflection;
using FrooxEngine;
using Elements.Core;

namespace Resonance;

public class ResonancePatcher : ResoniteMod
{
    public override string Name => "Resonite Mod Template";
    public override string Author => "You! :)";
    public override string Version => "1.0.0";
    public override string Link => "resonite.com";

    public override void OnEngineInit()
    {
        Harmony harmony = new("net.You.ResoniteModTemplate");
        var what = Engine.Current.WorldManager.FocusedWorld.LocalUser.LocalUserRoot;
        float3 huh = what.GetGlobalPosition(UserRoot.UserNode.Head);
    }
}
