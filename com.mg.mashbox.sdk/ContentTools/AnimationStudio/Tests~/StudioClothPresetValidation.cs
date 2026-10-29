using System;
using System.Linq;
using System.Reflection;
using MashBoxSDK.Cloth;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static class StudioClothPresetValidation
    {
        internal static void Run()
        {
            var source=ScriptableObject.CreateInstance<MashBoxClothProfile>();
            var target=ScriptableObject.CreateInstance<MashBoxClothProfile>();
            var preset=ScriptableObject.CreateInstance<MashBoxClothSettingsPreset>();
            var mesh=new Mesh();
            try
            {
                var excluded=new[]{"sourceMesh","motion","bodyCapsules","particleBudget","frequency","interpolateMotion","substeps","iterations","teleportDistance","preserveAuthoredShading","normalsEverySteps"};
                var settings=typeof(MashBoxClothProfile).GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)
                    .Where(f=>!excluded.Contains(f.Name)).ToArray();
                foreach(var field in settings)
                {
                    object value=field.FieldType==typeof(bool)?(object)false:
                        field.FieldType==typeof(int)?(object)7:
                        field.FieldType==typeof(float)?(object).123f:(object)new Vector3(1,2,3);
                    field.SetValue(source,value);
                }
                source.damping=3;source.bodyCollisions=true;
                target.sourceMesh=mesh;var weights=target.motion=new[]{0f,.2f,.4f};
                var colliders=target.bodyCapsules=new[]{new ClothBoneCapsule{startBone="LeftLeg",radius=.07f}};
                preset.Capture(source);preset.ApplyTo(target);
                foreach(var field in settings)
                    if(!Equals(field.GetValue(source),field.GetValue(target)))throw new Exception("Not copied: "+field.Name);
                if(target.sourceMesh!=mesh||!ReferenceEquals(weights,target.motion)||!ReferenceEquals(colliders,target.bodyCapsules))
                    throw new Exception("Preset replaced mesh-specific garment data.");
                target.damping=6;
                if(preset.damping!=3||source.damping!=3)throw new Exception("Preset shares mutable tuning with garment.");
                foreach(var name in excluded)
                    if(typeof(MashBoxClothSettingsPreset).GetField(name)!=null)throw new Exception("Preset stores garment data: "+name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(preset);UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
