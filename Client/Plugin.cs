using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Liquidwarp.UnlockGearProgression.Client;

[BepInPlugin(MyModInfo.MOD_GUID, MyModInfo.MOD_NAME, MyModInfo.MOD_VERSION)]
[BepInDependency("com.SPT.core", MyModInfo.CLIENT_SPT_VERSION)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    private void Awake()
    {
        Log = Logger;
        new Harmony(MyModInfo.MOD_GUID).PatchAll(typeof(Plugin).Assembly);
    }
}
