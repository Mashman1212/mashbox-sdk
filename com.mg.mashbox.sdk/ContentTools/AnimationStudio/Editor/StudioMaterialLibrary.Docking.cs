using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    internal sealed partial class StudioMaterialLibrary
    {
        [Serializable] internal sealed class Placement
        {
            public bool floating, tab, ownerContainer;
            public string anchorType, anchorTitle, side="Right";
            public int tabIndex;
            public float extent=360;
            public Rect bounds, anchorBounds;
        }
        private const string PlacementKey="Studio.MaterialLibrary.Placement";
        private static readonly BindingFlags Members=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly Type DockType=typeof(EditorWindow).Assembly.GetType("UnityEditor.DockArea");
        private static readonly Type SplitType=typeof(EditorWindow).Assembly.GetType("UnityEditor.SplitView");
        private static readonly Type DropType=typeof(EditorWindow).Assembly.GetType("UnityEditor.DropInfo");
        private static readonly FieldInfo ParentField=typeof(EditorWindow).GetField("m_Parent",Members);
        private bool restoringPlacement, closing;
        private static object Read(object obj,string name)=>obj==null?null:obj.GetType().GetProperty(name,Members)?.GetValue(obj)??obj.GetType().GetField(name,Members)?.GetValue(obj);
        private static void Write(object obj,string name,object value)
        {
            var property=obj.GetType().GetProperty(name,Members);
            if(property!=null){property.SetValue(obj,value);return;}
            obj.GetType().GetField(name,Members)?.SetValue(obj,value);
        }
        private static object Host(EditorWindow window)=>window?ParentField?.GetValue(window):null;
        private static object[] Children(object view)=>(Read(view,"children") as IEnumerable)?.Cast<object>().ToArray()??Array.Empty<object>();
        private static EditorWindow[] Panes(object dock)=>(Read(dock,"m_Panes") as IEnumerable)?.Cast<EditorWindow>().Where(w=>w).ToArray()??Array.Empty<EditorWindow>();
        private static object Container(EditorWindow window)=>Read(Host(window),"window");
        private static EditorWindow FirstPane(object branch)
        {
            var pane=Panes(branch).FirstOrDefault();if(pane)return pane;
            foreach(var child in Children(branch)){pane=FirstPane(child);if(pane)return pane;}return null;
        }
        internal Placement CapturePlacement()
        {
            var placement=new Placement{floating=true,bounds=position};var host=Host(this);
            if(host==null||DockType==null||!DockType.IsInstanceOfType(host))return placement;
            var peer=Panes(host).FirstOrDefault(w=>w!=this);
            if(peer){placement.tab=true;placement.tabIndex=Array.IndexOf(Panes(host),this);}
            else
            {
                var split=Read(host,"parent");var siblings=Children(split);int index=Array.IndexOf(siblings,host);
                if(split==null||!SplitType.IsInstanceOfType(split)||siblings.Length<2||index<0)return placement;
                int other=index+1<siblings.Length?index+1:index-1;peer=FirstPane(siblings[other]);if(!peer)return placement;
                bool vertical=Read(split,"vertical") is bool v&&v;
                placement.side=vertical?(index<other?"Top":"Bottom"):(index<other?"Left":"Right");
                var rect=(Rect)Read(host,"screenPosition");placement.extent=vertical?rect.height:rect.width;
            }
            placement.floating=false;placement.anchorType=peer.GetType().FullName;placement.anchorTitle=peer.titleContent.text;
            placement.anchorBounds=peer.position;placement.ownerContainer=owner&&ReferenceEquals(Container(peer),Container(owner));return placement;
        }
        private void SavePlacement()
        {
            if(restoringPlacement||closing||Host(this)==null)return;
            StudioPreferenceStore.SetString(PlacementKey,JsonUtility.ToJson(CapturePlacement()));
        }
        private Placement StoredPlacement()
        {
            try{return JsonUtility.FromJson<Placement>(StudioPreferenceStore.GetString(PlacementKey,""));}catch{return null;}
        }
        private EditorWindow FindAnchor(Placement placement)
        {
            if(placement==null)return owner;
            var candidates=Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w=>w&&w!=this&&Host(w)!=null&&w.GetType().FullName==placement.anchorType);
            if(placement.ownerContainer&&owner)candidates=candidates.Where(w=>ReferenceEquals(Container(w),Container(owner)));
            return candidates.OrderBy(w=>w.titleContent.text==placement.anchorTitle?0:1).ThenBy(w=>(w.position.center-placement.anchorBounds.center).sqrMagnitude).FirstOrDefault()??owner;
        }
        private void RestorePlacement()
        {
            if(!this)return;restoringPlacement=true;
            try
            {
                var stored=StoredPlacement();
                if(stored!=null&&stored.floating)
                {if(stored.bounds.width>=minSize.x&&stored.bounds.height>=minSize.y)position=stored.bounds;return;}
                var anchor=FindAnchor(stored);if(!anchor)return;
                if(stored!=null&&stored.tab&&TryDockTab(anchor,stored.tabIndex))return;
                if(TryDockBeside(anchor,stored?.side??"Right",stored?.extent??360))return;
                // Utility-only Studio hosts cannot participate in native docking.
                // Keep the library accessible beside that host without changing it.
                position=new Rect(anchor.position.xMax,anchor.position.y,360,Mathf.Max(360,anchor.position.height));
            }
            finally{EditorApplication.delayCall+=()=>{if(this)restoringPlacement=false;};}
        }
        private bool TryDockTab(EditorWindow anchor,int index)
        {
            var source=Host(this);var target=Host(anchor);
            if(source==null||target==null||!DockType.IsInstanceOfType(source)||!DockType.IsInstanceOfType(target))return false;
            var remove=DockType.GetMethod("RemoveTab",Members,null,new[]{typeof(EditorWindow),typeof(bool),typeof(bool)},null);
            var add=DockType.GetMethod("AddTab",Members,null,new[]{typeof(int),typeof(EditorWindow),typeof(bool)},null);
            if(remove==null||add==null)return false;
            remove.Invoke(source,new object[]{this,true,false});add.Invoke(target,new object[]{Mathf.Clamp(index,0,Panes(target).Length),this,true});return true;
        }
        internal bool TryDockBeside(EditorWindow anchor,string side,float extent)
        {
            var source=Host(this);var target=Host(anchor);var split=Read(target,"parent");
            if(source==null||target==null||DockType==null||SplitType==null||DropType==null||!DockType.IsInstanceOfType(source)||!DockType.IsInstanceOfType(target)||split==null||!SplitType.IsInstanceOfType(split))return false;
            var children=Children(split);int index=Array.IndexOf(children,target);if(index<0)return false;
            var edgeType=SplitType.GetNestedType("ViewEdge",Members);var extraType=SplitType.GetNestedType("ExtraDropInfo",Members);
            var perform=SplitType.GetMethod("PerformDrop",Members);var original=DockType.GetField("s_OriginalDragSource",Members);
            if(edgeType==null||extraType==null||perform==null||original==null)return false;
            var rect=(Rect)Read(target,"screenPosition");bool vertical=side=="Top"||side=="Bottom";
            extent=Mathf.Clamp(extent,vertical?minSize.y:minSize.x,Mathf.Max(vertical?minSize.y:minSize.x,(vertical?rect.height:rect.width)*.5f));
            if(vertical){if(side=="Bottom")rect.y=rect.yMax-extent;rect.height=extent;}else{if(side=="Right")rect.x=rect.xMax-extent;rect.width=extent;}
            var info=Activator.CreateInstance(DropType,Members,null,new[]{split},null);
            Write(info,"rect",rect);Write(info,"type",Enum.Parse(DropType.GetNestedType("Type",Members),"Pane"));
            Write(info,"userData",Activator.CreateInstance(extraType,Members,null,new object[]{false,Enum.Parse(edgeType,side),index},null));
            var previous=original.GetValue(null);
            try{original.SetValue(null,source);return perform.Invoke(split,new object[]{this,info,rect.center}) is bool result&&result;}
            finally{original.SetValue(null,previous);}
        }
    }
}
