using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace NOVR.PatchHelper;

public static class PatchLoader
{
    public static void Apply(Harmony harmony)
    {
        LogInfo("Applying...");
        var methods = Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(t => t.GetMethods(
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic));

        foreach (var method in methods)
        {
            HandleAttribute(method, harmony);
        }

        LogInfo("...Done.");
    }

    private static void HandleAttribute(MethodInfo method, Harmony harmony)
    {
        
        var attr = method.GetCustomAttribute<PatchAttribute>();
        if (attr == null) return;

        // One missing target (usually a game update renaming a method) disables only this patch.
        try
        {
            if (AccessTools.Method(attr.TargetType, attr.MethodName) == null)
            {
                NOVRLog.Error($"PatchLoader: {attr.TargetType}.{attr.MethodName} not found; {method.DeclaringType?.Name}.{method.Name} is disabled. " +
                              "This usually means the game updated and NOVR needs an update too.");
                return;
            }

            switch (attr)
            {
                case PatchPrefixAttribute prefixAttr:
                    HandlePrefixAttribute(prefixAttr, harmony, method);
                    break;

                case PatchPostfixAttribute postfixAttr:
                    HandlePostfixAttribute(postfixAttr, harmony, method);
                    break;
            }
        }
        catch (Exception exception)
        {
            NOVRLog.Error($"PatchLoader: failed to apply {method.DeclaringType?.Name}.{method.Name}; that feature is disabled.\n{exception}");
        }

    }

    private static void HandlePrefixAttribute(PatchAttribute attr, Harmony harmony, MethodInfo method)
    {
        var original = AccessTools.Method(
            attr.TargetType,
            attr.MethodName);
        LogInfo("Patching prefix for " + attr.TargetType + "." + method.Name);
        harmony.Patch(
            original,
            prefix: new HarmonyMethod(method));
    }

    private static void HandlePostfixAttribute(PatchAttribute attr, Harmony harmony, MethodInfo method)
    {
        var original = AccessTools.Method(
            attr.TargetType,
            attr.MethodName);

        LogInfo("Patching postfix for " + attr.TargetType + "." + method.Name);
        harmony.Patch(
            original,
            postfix: new HarmonyMethod(method));
    }

    private static void LogInfo(object log) => NOVRLog.Info($"PatchLoader: {log}");
}