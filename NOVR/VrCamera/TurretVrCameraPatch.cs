using System;
using HarmonyLib;
using UnityEngine;

namespace NOVR.VrCamera;

internal static class TurretVrCameraPatch
{
    [HarmonyPatch(typeof(global::Turret), "FixedUpdate")]
    private static class FixedUpdatePatch
    {
        // Runs for every turret every physics tick, so use compiled field accessors instead of Traverse.
        private static readonly AccessTools.FieldRef<global::Turret, bool> ManualField =
            AccessTools.FieldRefAccess<global::Turret, bool>("manual");
        private static readonly AccessTools.FieldRef<global::Turret, global::Unit> TargetField =
            AccessTools.FieldRefAccess<global::Turret, global::Unit>("target");
        private static readonly AccessTools.FieldRef<global::Turret, global::Aircraft> AircraftField =
            AccessTools.FieldRefAccess<global::Turret, global::Aircraft>("aircraft");
        private static readonly AccessTools.FieldRef<global::Turret, global::Unit> AttachedUnitField =
            AccessTools.FieldRefAccess<global::Turret, global::Unit>("attachedUnit");
        private static readonly AccessTools.FieldRef<global::Turret, float> LastVectorSentField =
            AccessTools.FieldRefAccess<global::Turret, float>("lastVectorSent");
        private static readonly AccessTools.FieldRef<global::Turret, global::WeaponStation> CurrentWeaponStationField =
            AccessTools.FieldRefAccess<global::Turret, global::WeaponStation>("currentWeaponStation");
        private static readonly AccessTools.FieldRef<global::Turret, Vector3> ManualVectorField =
            AccessTools.FieldRefAccess<global::Turret, Vector3>("manualVector");
        private static readonly Action<global::Turret, Vector3> AimTurret =
            AccessTools.MethodDelegate<Action<global::Turret, Vector3>>(
                AccessTools.Method(typeof(global::Turret), "AimTurret", new[] { typeof(Vector3) }));

        [HarmonyPrefix]
        private static bool Prefix(global::Turret __instance)
        {
            if (__instance == null)
            {
                return true;
            }

            if (!ManualField(__instance) || TargetField(__instance) != null)
            {
                return true;
            }

            var aircraft = AircraftField(__instance);
            var attachedUnit = AttachedUnitField(__instance);
            if (aircraft == null ||
                attachedUnit == null ||
                !aircraft.LocalSim ||
                global::SceneSingleton<global::CameraStateManager>.i.currentState != global::SceneSingleton<global::CameraStateManager>.i.cockpitState)
            {
                return true;
            }

            var vrCamera = APIBus.MainCamera;
            if (vrCamera == null)
            {
                return true;
            }

            __instance.SetVector(vrCamera.transform.forward);

            var lastVectorSent = LastVectorSentField(__instance);
            if (Time.timeSinceLevelLoad - lastVectorSent > 0.20000000298023224)
            {
                var currentWeaponStation = CurrentWeaponStationField(__instance);
                var manualVector = ManualVectorField(__instance);
                aircraft.SetTurretVector(currentWeaponStation.Number, manualVector);
                LastVectorSentField(__instance) = Time.timeSinceLevelLoad;
            }

            AimTurret(__instance, ManualVectorField(__instance));
            return false;
        }
    }
}
