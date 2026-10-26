using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal static partial class StudioTheme
    {
        internal sealed partial class Palette
        {
            // Explicit surface roles let a palette express a real tonal hierarchy
            // rather than applying the same tint and offsets to every interface.
            internal Palette(string id, string name, string canvas, string panel, string header,
                string button, string hover, string accent, string text, string muted,
                string border, string selection, string rail, string toolSelection)
            {
                Id=id;Name=name;Canvas=Hex(canvas);Panel=Hex(panel);Header=Hex(header);
                Button=Hex(button);Hover=Hex(hover);Accent=Hex(accent);Text=Hex(text);
                Muted=Hex(muted);Border=Hex(border);Selection=Hex(selection);
                ToolRail=Hex(rail);ToolSelection=Hex(toolSelection);
                Grid=Color.Lerp(Panel,Text,.35f);UVWire=Color.Lerp(Accent,Text,.20f);UVSelected=Accent;
            }
            internal Palette(Color primary, Color secondary)
            {
                Id="custom";Name="Custom";primary.a=secondary.a=1;
                Canvas=primary;Accent=secondary;
                Text=TextContrast(Color.black,Canvas)>=TextContrast(Color.white,Canvas)?Color.black:Color.white;
                bool light=Text==Color.black;
                Panel=FitSurface(Color.Lerp(Canvas,Color.white,light ? .12f : .07f),Text);
                Header=FitSurface(Color.Lerp(Canvas,Color.black,light ? .09f : .18f),Text);
                Button=FitSurface(Color.Lerp(Panel,light ? Color.black : Color.white,.10f),Text);
                Hover=FitSurface(Color.Lerp(Button,Accent,.16f),Text);
                Selection=FitSurface(Color.Lerp(Panel,Accent,.35f),Text);
                ToolRail=FitSurface(Color.Lerp(Canvas,Color.black,.16f),Text);
                ToolSelection=FitSurface(Color.Lerp(ToolRail,Accent,.38f),Text);
                Muted=Color.Lerp(Panel,Text,.78f);
                for(int i=0;i<16&&TextContrast(Muted,Panel)<4.5f;i++)Muted=Color.Lerp(Muted,Text,.25f);
                Border=Color.Lerp(Panel,Text,.35f);Grid=Color.Lerp(Panel,Text,.36f);
                UVWire=Color.Lerp(Accent,Text,.25f);UVSelected=Accent;
            }
            private static Color Hex(string hex)
            { ColorUtility.TryParseHtmlString("#"+hex,out var color);return color; }
        }
        private static float Luminance(Color color)
        {
            float Linear(float c)=>c<=.04045f?c/12.92f:Mathf.Pow((c+.055f)/1.055f,2.4f);
            return Linear(color.r)*.2126f+Linear(color.g)*.7152f+Linear(color.b)*.0722f;
        }
        internal static float TextContrast(Color a,Color b)
        {float x=Luminance(a),y=Luminance(b);return (Mathf.Max(x,y)+.05f)/(Mathf.Min(x,y)+.05f);}
        private static Color FitSurface(Color surface,Color text)
        {
            if(TextContrast(text,surface)>=4.5f)return surface;
            Color end=text==Color.white?Color.black:Color.white;
            float low=0,high=1;
            for(int i=0;i<16;i++)
            {
                float t=(low+high)*.5f;
                if(TextContrast(text,Color.Lerp(surface,end,t))>=4.51f)high=t;else low=t;
            }
            return Color.Lerp(surface,end,high);
        }
        private static Palette customPalette;
        private static Color customPrimary,customSecondary;
        private static readonly HashSet<Palette> retiredCustom = new HashSet<Palette>();
        internal static Palette Custom
        {
            get
            {
                var settings=StudioSettings.Current;
                if(customPalette!=null&&customPrimary==settings.customPrimary&&customSecondary==settings.customSecondary)return customPalette;
                if(customPalette!=null)
                {
                    retiredCustom.Add(customPalette);
                    EditorApplication.delayCall-=ReleaseRetiredCustom;
                    EditorApplication.delayCall+=ReleaseRetiredCustom;
                }
                customPrimary=settings.customPrimary;customSecondary=settings.customSecondary;
                return customPalette=new Palette(customPrimary,customSecondary);
            }
        }
        // Dispose only retired custom resources, after any active GUI skin scope.
        // Color-picker drags must not retain one GUISkin/texture set per sample.
        private static void ReleaseRetiredCustom()
        {
            foreach(var key in Cache.Keys.Where(k=>retiredCustom.Contains(k.palette)).ToArray())
            {DestroyResources(Cache[key]);Cache.Remove(key);}
            retiredCustom.Clear();
        }
        private static void DestroyResources(Resources resource)
        {
            if(resource.Skin)UnityEngine.Object.DestroyImmediate(resource.Skin);
            foreach(var texture in resource.Textures.Values)if(texture)UnityEngine.Object.DestroyImmediate(texture);
            foreach(var texture in resource.FieldTextures.Values)if(texture)UnityEngine.Object.DestroyImmediate(texture);
            foreach(var texture in resource.DropdownTextures.Values)if(texture)UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
