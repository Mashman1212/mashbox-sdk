using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MashBoxSDK.Utility
{
    internal static class MBFmodReflection
    {
        private static Type runtimeManagerType;
        private static Type runtimeUtilsType;
        private static Type studioListenerType;
        private static Type stopModeType;

        public static Type GetStudioListenerType()
        {
            return studioListenerType ??= FindType("FMODUnity.StudioListener");
        }

        public static bool AddStudioListener(GameObject target)
        {
            if (target == null)
                return false;

            var listenerType = GetStudioListenerType();
            if (listenerType == null)
                return false;

            if (target.GetComponent(listenerType) != null)
                return true;

            target.AddComponent(listenerType);
            return true;
        }

        public static bool LoadBank(string bankName, bool loadSamples = true)
        {
            return InvokeRuntimeManager("LoadBank", new object[] { bankName, loadSamples }) ||
                   InvokeRuntimeManager("LoadBank", new object[] { bankName });
        }

        public static bool UnloadBank(string bankName)
        {
            return InvokeRuntimeManager("UnloadBank", new object[] { bankName, true }) ||
                   InvokeRuntimeManager("UnloadBank", new object[] { bankName });
        }

        public static object CreateInstance(string eventPath)
        {
            if (string.IsNullOrWhiteSpace(eventPath))
                return null;

            var managerType = GetRuntimeManagerType();
            if (managerType == null)
                return null;

            try
            {
                return InvokeBestMethod(managerType, null, "CreateInstance", new object[] { eventPath });
            }
            catch
            {
                return null;
            }
        }

        public static void Set3DAttributes(object eventInstance, GameObject sourceObject, Rigidbody sourceBody)
        {
            if (eventInstance == null || sourceObject == null)
                return;

            var utilsType = GetRuntimeUtilsType();
            if (utilsType == null)
                return;

            try
            {
                var attributes = InvokeBestMethod(utilsType, null, "To3DAttributes", new object[] { sourceObject, sourceBody });
                if (attributes == null)
                    attributes = InvokeBestMethod(utilsType, null, "To3DAttributes", new object[] { sourceObject.transform });
                if (attributes == null)
                    attributes = InvokeBestMethod(utilsType, null, "To3DAttributes", new object[] { sourceObject });
                if (attributes == null)
                    return;

                InvokeBestMethod(eventInstance.GetType(), eventInstance, "set3DAttributes", new[] { attributes });
            }
            catch
            {
                // Optional FMOD integration only.
            }
        }

        public static void SetParameter(object eventInstance, string parameterName, float value)
        {
            if (eventInstance == null || string.IsNullOrWhiteSpace(parameterName))
                return;

            try
            {
                var result = InvokeBestMethod(eventInstance.GetType(), eventInstance, "setParameterByName", new object[] { parameterName, value, false });
                if (result == null)
                    InvokeBestMethod(eventInstance.GetType(), eventInstance, "setParameterByName", new object[] { parameterName, value });
            }
            catch
            {
                // Optional FMOD integration only.
            }
        }

        public static void SetVolume(object eventInstance, float volume)
        {
            if (eventInstance == null)
                return;

            try
            {
                InvokeBestMethod(eventInstance.GetType(), eventInstance, "setVolume", new object[] { volume });
            }
            catch
            {
                // Optional FMOD integration only.
            }
        }

        public static void Start(object eventInstance)
        {
            if (eventInstance == null)
                return;

            InvokeBestMethod(eventInstance.GetType(), eventInstance, "start", Array.Empty<object>());
        }

        public static void Release(object eventInstance)
        {
            if (eventInstance == null)
                return;

            InvokeBestMethod(eventInstance.GetType(), eventInstance, "release", Array.Empty<object>());
        }

        public static void Stop(object eventInstance, bool immediate)
        {
            if (eventInstance == null)
                return;

            var stopType = stopModeType ??= FindType("FMOD.Studio.STOP_MODE");
            if (stopType == null)
                return;

            var modeName = immediate ? "IMMEDIATE" : "ALLOWFADEOUT";
            var modeValue = Enum.Parse(stopType, modeName);
            InvokeBestMethod(eventInstance.GetType(), eventInstance, "stop", new[] { modeValue });
        }

        private static Type GetRuntimeManagerType()
        {
            return runtimeManagerType ??= FindType("FMODUnity.RuntimeManager");
        }

        private static Type GetRuntimeUtilsType()
        {
            return runtimeUtilsType ??= FindType("FMODUnity.RuntimeUtils");
        }

        private static bool InvokeRuntimeManager(string methodName, object[] args)
        {
            var managerType = GetRuntimeManagerType();
            if (managerType == null)
                return false;

            try
            {
                return InvokeBestMethod(managerType, null, methodName, args) != null;
            }
            catch
            {
                return false;
            }
        }

        private static object InvokeBestMethod(Type type, object target, string methodName, object[] args)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var methods = type.GetMethods(flags).Where(method => method.Name == methodName).ToArray();
            if (methods.Length == 0)
                return null;

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length != args.Length)
                    continue;

                var convertedArgs = new object[args.Length];
                var compatible = true;

                for (var i = 0; i < args.Length; i++)
                {
                    if (!TryConvertArgument(args[i], parameters[i].ParameterType, out convertedArgs[i]))
                    {
                        compatible = false;
                        break;
                    }
                }

                if (!compatible)
                    continue;

                return method.Invoke(target, convertedArgs);
            }

            return null;
        }

        private static bool TryConvertArgument(object value, Type targetType, out object convertedValue)
        {
            convertedValue = value;

            if (value == null)
                return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;

            var valueType = value.GetType();
            if (targetType.IsAssignableFrom(valueType))
                return true;

            try
            {
                if (targetType.IsEnum && value is string stringValue)
                {
                    convertedValue = Enum.Parse(targetType, stringValue);
                    return true;
                }

                if (targetType == typeof(float))
                {
                    convertedValue = Convert.ToSingle(value);
                    return true;
                }

                if (targetType == typeof(bool))
                {
                    convertedValue = Convert.ToBoolean(value);
                    return true;
                }

                if (targetType == typeof(int))
                {
                    convertedValue = Convert.ToInt32(value);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try
                {
                    type = assembly.GetType(fullName, false);
                }
                catch
                {
                    // Ignore dynamic/broken assemblies.
                }

                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
