using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using System;

namespace UserInfoList
{
    public class UserInfoList : ResoniteMod
    {
        public override string Name => "UserInfoList";
        public override string Author => "Dexy";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/Dexy/UserInfoList";

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("net.dexy.userinfolist");
            harmony.PatchAll();
            Msg("UserInfoList mod loaded successfully!");
        }
    }

    [HarmonyPatch(typeof(UserRoot))]
    public static class UserRootPatches
    {
        [HarmonyPatch("OnStart")]
        [HarmonyPostfix]
        public static void OnStart_Postfix(UserRoot __instance)
        {
            try
            {
                // Initialize UserList functionality directly without component
                UserListManager.Initialize(__instance);
                Elements.Core.UniLog.Log($"UserList initialized for user: {__instance.ActiveUser?.UserName ?? "Unknown"}");
            }
            catch (Exception ex)
            {
                Elements.Core.UniLog.Error($"Failed to initialize UserList: {ex}");
            }
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPrefix]
        public static void OnDestroy_Prefix(UserRoot __instance)
        {
            try
            {
                // Clean up UserList functionality
                UserListManager.Cleanup(__instance);
                Elements.Core.UniLog.Log($"UserList cleaned up for user: {__instance.ActiveUser?.UserName ?? "Unknown"}");
            }
            catch (Exception ex)
            {
                Elements.Core.UniLog.Error($"Failed to cleanup UserList: {ex}");
            }
        }
    }

    // Optional: Patch to ensure UserList works with different UserRoot scenarios
    [HarmonyPatch(typeof(UserRoot), "OnAwake")]
    public static class UserRootAwakePatch
    {
        [HarmonyPostfix]
        public static void OnAwake_Postfix(UserRoot __instance)
        {
            try
            {
                // Ensure the UserRoot is properly initialized before we add our functionality
                Elements.Core.UniLog.Log($"UserRoot awakened for user: {__instance.ActiveUser?.UserName ?? "Pending"}");
            }
            catch (Exception ex)
            {
                Elements.Core.UniLog.Error($"Error in UserRoot OnAwake patch: {ex}");
            }
        }
    }
}
