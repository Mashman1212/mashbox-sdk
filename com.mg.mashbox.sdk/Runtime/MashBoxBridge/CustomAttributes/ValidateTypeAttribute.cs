using System;
using UnityEngine;

namespace MashBoxBridge.CustomAttributes
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ValidateTypeAttribute : PropertyAttribute
    {
        public Type PropertyType { get; }
        public bool AllowSceneObjects { get; }

        public ValidateTypeAttribute(Type propertyType, bool allowSceneObjects = false)
        {
            PropertyType = propertyType;
            AllowSceneObjects = allowSceneObjects;
        }
    }
}