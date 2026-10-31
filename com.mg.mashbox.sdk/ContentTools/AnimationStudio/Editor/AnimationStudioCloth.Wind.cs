using System;
using MashBoxSDK.Cloth;
using UnityEditor;
using UnityEngine;
namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        [SerializeField] private bool clothFlutterPaint, clothWindTunnel;
        [SerializeField] private float clothFlutterValue=1, clothWindSpeed=4, clothWindHeading, clothWindElevation, clothWindGust=.25f, clothWindFrequency=.5f;
        private float clothWindTime;
        private Mesh clothFlutterOutput;
        private int clothFlutterHash;
        private readonly MaterialPropertyBlock clothWindBlock=new MaterialPropertyBlock();
        private MashBoxWindSettings ClothWindSettings => new MashBoxWindSettings {
            velocity=clothWindTunnel?Quaternion.Euler(-clothWindElevation,clothWindHeading,0)*Vector3.forward*clothWindSpeed:Vector3.zero,
            gust=clothWindGust, frequency=clothWindFrequency };
        private float[] ClothPaintValues
        {
            get { if(clothFlutterPaint){clothProfile.EnsureFlutter();return clothProfile.flutter;}return clothProfile.motion; }
        }
        private void DrawClothWindToolbar()
        {
            using(new EditorGUI.DisabledScope(!CanSimulateCloth))
            {
                if(GUILayout.Button(new GUIContent("Flutter", "Paint the red vertex channel. Zero stays still; one receives full wind flutter."),clothFlutterPaint&&clothPainting?EditorStyles.miniButtonMid:EditorStyles.miniButton))
                {
                    bool wasFlutter=clothFlutterPaint&&clothPainting;
                    EndClothStroke();clothFlutterPaint=true;clothPainting=!wasFlutter;clothTool=0;
                    clothProfile.EnsureFlutter();
                    if(clothSession==null){bool paint=clothPainting;StartClothSession();clothPainting=paint;}
                    clothRunning=false;viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);
                }
            }
            bool tunnel=GUILayout.Toggle(clothWindTunnel,"Wind tunnel",EditorStyles.miniButton);
            if(tunnel!=clothWindTunnel){clothWindTunnel=tunnel;UpdateClothWindPreview();viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);}
            if(clothWindTunnel)
            {
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("Speed",GUILayout.Width(35));clothWindSpeed=GUILayout.HorizontalSlider(clothWindSpeed,0,25,GUILayout.Width(75));
                GUILayout.Label(clothWindSpeed.ToString("0.0")+" m/s",GUILayout.Width(55));
                if(EditorGUI.EndChangeCheck())UpdateClothWindPreview();
            }
        }
        private void DrawClothWindInspector()
        {
            if(!clothWindTunnel)return;
            GUILayout.Label("WIND TUNNEL",EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            clothWindSpeed=EditorGUILayout.Slider("Wind speed (m/s)",clothWindSpeed,0,25);
            clothWindHeading=EditorGUILayout.Slider("Heading",clothWindHeading,-180,180);
            clothWindElevation=EditorGUILayout.Slider("Elevation",clothWindElevation,-90,90);
            clothWindGust=EditorGUILayout.Slider("Gust strength",clothWindGust,0,1);
            clothWindFrequency=EditorGUILayout.Slider("Gust frequency (Hz)",clothWindFrequency,0,3);
            if(EditorGUI.EndChangeCheck()){UpdateClothWindPreview();viewport.MarkDirty(MashBoxStudioViewport.DirtyState.Scene);}
            EditorGUILayout.HelpBox("Shared preview airflow drives simulation and flutter. Per-garment Wind Response controls simulation strength. Play advances wind; Pause freezes it.",MessageType.None);
        }
        private void UpdateClothWindPreview()
        {
            var settings=ClothWindSettings;
            if(clothSession!=null)
            {
                clothSession.WindVelocity=settings.Sample(clothWindTime);
                if(clothProfile)
                {
                    clothProfile.EnsureFlutter();
                    int hash=17;unchecked{foreach(float value in clothProfile.flutter)hash=hash*31+value.GetHashCode();}
                    if(clothFlutterOutput!=clothSession.Output || clothFlutterHash!=hash)
                    {clothProfile.ApplyFlutter(clothSession.Output);clothFlutterOutput=clothSession.Output;clothFlutterHash=hash;}
                }
                Apply(clothSession.OutputRenderer,clothProfile?clothProfile.flutterAmplitude:0);
            }
            // Override only preview renderers: never set global shader state in the editor.
            for(int i=0;i<clothRenderers.Length;i++)Apply(clothRenderers[i],0);
            void Apply(Renderer renderer,float amplitude)
            {
                if(!renderer)return;
                renderer.GetPropertyBlock(clothWindBlock);
                var velocity=settings.velocity;
                clothWindBlock.SetVector("_MGWindPreview",new Vector4(velocity.x,velocity.y,velocity.z,1));
                clothWindBlock.SetVector("_MGWindPreviewParams",new Vector4(settings.gust,settings.frequency,clothWindTime,0));
                clothWindBlock.SetFloat("_MGFlutterAmplitude",clothPainting?0:amplitude);
                renderer.SetPropertyBlock(clothWindBlock);
            }
        }
    }
}
