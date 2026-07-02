#if UNITY_EDITOR

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace MashBoxSDK.SDKMain
{
    [InitializeOnLoad]
    public static class MashBoxInputSystemSetup
    {
        private const string InputSystemPackageName = "com.unity.inputsystem";
        private const string ProjectSettingsAssetPath = "ProjectSettings/ProjectSettings.asset";
        private static ListRequest packageListRequest;
        private static AddRequest packageAddRequest;
        private static bool inputSystemPackageInstalled;
        private static bool inputSystemEnabled;
        private static bool inputSystemEnabledKnown;
        private static bool packageStateKnown;
        private static double nextInputHandlerCheckTime;
        private static string lastStatusMessage = string.Empty;
        private const double InputHandlerRefreshInterval = 2.0d;

        static MashBoxInputSystemSetup()
        {
            RefreshPackageState();
        }

        public static bool IsInputSystemPackageInstalled
        {
            get
            {
                UpdateRequests();
                return inputSystemPackageInstalled;
            }
        }

        public static bool IsInputSystemEnabled
        {
            get
            {
                UpdateInputHandlerState();
                return inputSystemEnabled;
            }
        }

        public static bool HasInputSystemReady => IsInputSystemPackageInstalled && IsInputSystemEnabled;
        public static bool IsBusy => (packageListRequest != null && !packageListRequest.IsCompleted) || (packageAddRequest != null && !packageAddRequest.IsCompleted);
        public static bool ShouldShowSetupAlert => !HasInputSystemReady;
        public static string LastStatusMessage => lastStatusMessage;

        public static void RefreshPackageState()
        {
            if (packageListRequest != null && !packageListRequest.IsCompleted)
                return;

            packageStateKnown = false;
            lastStatusMessage = "Checking Input System package...";
            packageListRequest = Client.List(true);
        }

        public static void UpdateRequests()
        {
            if (packageListRequest != null && packageListRequest.IsCompleted)
            {
                if (packageListRequest.Status == StatusCode.Success)
                {
                    inputSystemPackageInstalled = packageListRequest.Result.Any(package => package.name == InputSystemPackageName);
                    packageStateKnown = true;
                    if (inputSystemPackageInstalled && string.IsNullOrEmpty(lastStatusMessage))
                        lastStatusMessage = string.Empty;
                    else if (!inputSystemPackageInstalled)
                        lastStatusMessage = "The Unity Input System package is not installed.";
                }
                else
                {
                    inputSystemPackageInstalled = false;
                    packageStateKnown = false;
                    lastStatusMessage = $"Unable to check packages: {packageListRequest.Error?.message}";
                }

                packageListRequest = null;
            }

            if (packageAddRequest != null && packageAddRequest.IsCompleted)
            {
                lastStatusMessage = packageAddRequest.Status == StatusCode.Success
                    ? "Input System installed. Enable it below, then let Unity restart the editor."
                    : $"Failed to install Input System: {packageAddRequest.Error?.message}";

                packageAddRequest = null;
                RefreshPackageState();
            }
        }

        public static void InstallInputSystemPackage()
        {
            if (packageAddRequest != null && !packageAddRequest.IsCompleted)
                return;

            lastStatusMessage = "Installing Input System package...";
            packageAddRequest = Client.Add(InputSystemPackageName);
        }

        public static bool EnableInputSystem()
        {
            var inputHandlerProperty = GetActiveInputHandlerProperty();
            if (inputHandlerProperty == null)
            {
                lastStatusMessage = "Could not find the Active Input Handling setting in Project Settings.";
                return false;
            }

            if (inputHandlerProperty.intValue == 1 || inputHandlerProperty.intValue == 2)
            {
                lastStatusMessage = "Input System is already enabled.";
                return true;
            }

            inputHandlerProperty.intValue = 2;
            inputHandlerProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            inputSystemEnabled = true;
            inputSystemEnabledKnown = true;
            nextInputHandlerCheckTime = 0d;
            lastStatusMessage = "Enabled Both input backends. Unity may ask to restart the editor.";
            return true;
        }

        public static string GetStatusSummary()
        {
            UpdateRequests();

            if (!packageStateKnown && IsBusy)
                return "Checking Input System package...";

            if (!IsInputSystemPackageInstalled)
                return "Input System package missing";

            return IsInputSystemEnabled
                ? "Input System enabled"
                : "Input System disabled in Player Settings";
        }

        private static SerializedProperty GetActiveInputHandlerProperty()
        {
            try
            {
                var projectSettingsAsset = AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsAssetPath).FirstOrDefault();
                if (projectSettingsAsset == null)
                    return null;

                var projectSettings = new SerializedObject(projectSettingsAsset);
                return projectSettings.FindProperty("activeInputHandler");
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void UpdateInputHandlerState(bool force = false)
        {
            if (!force && inputSystemEnabledKnown && EditorApplication.timeSinceStartup < nextInputHandlerCheckTime)
                return;

            nextInputHandlerCheckTime = EditorApplication.timeSinceStartup + InputHandlerRefreshInterval;

            var inputHandlerProperty = GetActiveInputHandlerProperty();
            if (inputHandlerProperty == null)
            {
                inputSystemEnabled = false;
                inputSystemEnabledKnown = true;
                return;
            }

            var activeHandler = inputHandlerProperty.intValue;
            inputSystemEnabled = activeHandler == 1 || activeHandler == 2;
            inputSystemEnabledKnown = true;
        }
    }
}

#endif
