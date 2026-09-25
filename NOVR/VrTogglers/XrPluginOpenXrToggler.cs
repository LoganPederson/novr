using System;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using NOVR.VrUi.Hands;

namespace NOVR.VrTogglers;

public class XrPluginOpenXrToggler : XrPluginToggler
{
    protected override bool SetUp()
    {
        if (ModConfiguration.Instance != null && ModConfiguration.Instance.EnableExperimentalSteamVrControllerProfiles.Value)
        {
            try
            {
                OpenXrControllerProfileBootstrap.ConfigureSteamVrControllerProfiles();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[NOVR] Failed to configure experimental SteamVR OpenXR controller profiles. Continuing normal VR startup. Exception: {exception}");
            }
        }

        if (ModConfiguration.Instance != null && ModConfiguration.Instance.EnableHandTracking.Value)
        {
            try
            {
                OpenXrControllerProfileBootstrap.EnsureCustomFeature<OpenXrHandTrackingFeature>(
                    OpenXrHandTrackingFeature.UiName, OpenXrHandTrackingFeature.ExtensionStrings);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[NOVR] Failed to register hand tracking. Continuing normal VR startup. Exception: {exception}");
            }
        }

        return base.SetUp();
    }

    protected override XRLoader CreateLoader()
    {
        var xrLoader = ScriptableObject.CreateInstance<OpenXRLoader>();
        return xrLoader;
    }
}
