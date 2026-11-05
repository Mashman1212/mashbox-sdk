using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace MashBoxSDK.AnimationStudio
{
    [Serializable] internal sealed class StudioCustomToolbarDefinition
    {
        public string id, name;
        public List<string> commands = new List<string>();
    }
    internal static class StudioCustomToolbars
    {
        internal const string PreferenceKey = "Studio.CustomToolbars";
        internal const string SpacerKey = "Toolbar/Spacer";
        private static IEnumerable<Type> ControlTypes()
        {
            try { return typeof(StudioCustomToolbars).Assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { return ex.Types.Where(type => type != null); }
        }
        private static readonly Dictionary<string, Type> controls = ControlTypes()
            .Select(type => new { type, attribute = type.GetCustomAttributes(typeof(EditorToolbarElementAttribute), false).Cast<EditorToolbarElementAttribute>().FirstOrDefault() })
            .Where(x => x.attribute != null && x.attribute.targetWindows.Contains(typeof(MashBoxStudioViewport)))
            .ToDictionary(x => "Control/" + x.attribute.id, x => x.type);
        internal static IEnumerable<StudioModelEditor.ToolbarCommand> NativeControls => controls.Keys.OrderBy(x => x).Select(key => new StudioModelEditor.ToolbarCommand {
            Key = key, Path = "Toolbar Controls/" + key.Substring("Control/MashBox/".Length), Label = key.Substring(key.LastIndexOf('/') + 1), Enabled = true });
        internal static VisualElement CreateControl(string key, MashBoxStudioViewport view)
        {
            if (!controls.TryGetValue(key, out var type)) return null;
            var control = (VisualElement)Activator.CreateInstance(type);
            if (control is StudioToolbarOrientation orientation) orientation.IconOnly = true;
            if (control is IAccessContainerWindow access) access.containerWindow = view;
            return control;
        }
        [Serializable] private sealed class Collection { public List<StudioCustomToolbarDefinition> items = new List<StudioCustomToolbarDefinition>(); }
        internal static List<StudioCustomToolbarDefinition> Read()
        {
            string raw = StudioPreferenceStore.GetString(PreferenceKey);
            if (string.IsNullOrEmpty(raw)) return new List<StudioCustomToolbarDefinition>();
            try
            {
                var items = JsonUtility.FromJson<Collection>(raw)?.items ?? new List<StudioCustomToolbarDefinition>();
                var seen = new HashSet<string>();
                items.RemoveAll(x => x == null || string.IsNullOrEmpty(x.id) || !x.id.StartsWith("MashBox/Custom/", StringComparison.Ordinal) || !seen.Add(x.id));
                foreach (var item in items) { if (item.commands == null) item.commands = new List<string>(); item.commands.RemoveAll(key => string.IsNullOrEmpty(key) || key == "Mesh Display/Recalculate Normals"); if (string.IsNullOrWhiteSpace(item.name)) item.name = "Custom Toolbar"; }
                return items;
            }
            catch (ArgumentException) { return new List<StudioCustomToolbarDefinition>(); }
        }
        internal static StudioCustomToolbarDefinition Create(string name) => new StudioCustomToolbarDefinition { id = "MashBox/Custom/" + Guid.NewGuid().ToString("N"), name = name };
        internal static void Save(List<StudioCustomToolbarDefinition> items)
        {
            StudioPreferenceStore.SetString(PreferenceKey, JsonUtility.ToJson(new Collection { items = items }));
            foreach (var view in Resources.FindObjectsOfTypeAll<MashBoxStudioViewport>()) view.ReloadCustomToolbars();
        }
    }
    // A native toolbar overlay, so docking/visibility/layout use the existing Studio path.
    [Serializable] internal abstract class StudioCommandToolbar : ToolbarOverlay, ICreateHorizontalToolbar, ICreateVerticalToolbar
    {
        private static readonly System.Reflection.MethodInfo NativeInitialize = typeof(Overlay).GetMethod("Initialize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(OverlayAttribute) }, null);
        private StudioCustomToolbarDefinition definition;
        private string signature;
        private VisualElement contents;
        private bool? contentVertical;
        protected virtual bool MenuGroupSeparators => false;
        internal void Configure(StudioCustomToolbarDefinition value)
        {
            string next = JsonUtility.ToJson(value);
            definition = value;
            // Dynamic overlays need the same native metadata initialization as declared
            // overlays, but no declaration attribute (which would spawn an extra instance).
            if (!containerWindow && id != value.id)
            {
                if (NativeInitialize == null) throw new NotSupportedException("This Unity version does not expose dynamic overlay initialization.");
                NativeInitialize.Invoke(this, new object[] { new OverlayAttribute(typeof(MashBoxStudioViewport), value.id, value.name, true) {
                    defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top, defaultDockIndex = int.MaxValue, defaultLayout = Layout.HorizontalToolbar } });
            }
            displayName = value.name;
            if (signature != next) { signature = next; Rebuild(); }
        }
        public override void OnCreated()
        {
            base.OnCreated();
            layoutChanged += OnLayout;
            StudioShortcuts.changed += Refresh;
            if (containerWindow is MashBoxStudioViewport view) view.StudioToolbarStateChanged += Refresh;
        }
        public override void OnWillBeDestroyed()
        {
            layoutChanged -= OnLayout;
            StudioShortcuts.changed -= Refresh;
            if (containerWindow is MashBoxStudioViewport view) view.StudioToolbarStateChanged -= Refresh;
            base.OnWillBeDestroyed();
        }
        private void OnLayout(Layout value) {contentVertical=value==Layout.VerticalToolbar;Direction();}
        private void Direction() => Direction(contentVertical??layout==Layout.VerticalToolbar);
        private void Direction(bool vertical)
        {
            if(contents==null)return;
            contents.style.flexDirection=vertical?FlexDirection.Column:FlexDirection.Row;
            contents.style.flexGrow=0;contents.style.flexShrink=0;contents.style.alignSelf=Align.FlexStart;
            foreach(var gap in contents.Children().Where(e=>e.name=="studio-toolbar-menu-separator"||e.name=="studio-toolbar-spacer"))
            {
                // Bound cross-axis size to one button footprint. Percentage/auto
                // cross sizing can feed a wide row back into a native side dock.
                float extent=StudioSettings.Current.toolbarIconSize+6;
                gap.style.alignSelf=Align.Center;
                gap.style.width=vertical?extent:8;gap.style.height=vertical?8:extent;
                gap[0].style.width=vertical?extent:1;gap[0].style.height=vertical?1:extent;
            }
        }
        private OverlayToolbar CreateToolbar(bool vertical)
        {
            contentVertical=vertical;
            var toolbar = new OverlayToolbar();
            toolbar.Add(CreatePanelContent());
            Direction(vertical);
            return toolbar;
        }
        OverlayToolbar ICreateHorizontalToolbar.CreateHorizontalToolbarContent() => CreateToolbar(false);
        OverlayToolbar ICreateVerticalToolbar.CreateVerticalToolbarContent() => CreateToolbar(true);
        public override VisualElement CreatePanelContent()
        {
            contents = new VisualElement { name = "custom-toolbar-contents" };
            contents.RegisterCallback<PointerEnterEvent>(_ => Refresh());
            Rebuild(); return contents;
        }
        private static VisualElement CreateSeparator(string name,string key,bool automatic)
        {
            var gap=new VisualElement{name=name,userData=key,pickingMode=PickingMode.Ignore};
            gap.style.display=automatic?DisplayStyle.None:DisplayStyle.Flex;gap.style.flexShrink=0;gap.style.alignItems=Align.Center;gap.style.justifyContent=Justify.Center;
            var line=new VisualElement{pickingMode=PickingMode.Ignore};var tint=StudioTheme.Current.Text;tint.a=.25f;line.style.backgroundColor=tint;gap.Add(line);
            return gap;
        }
        private void Rebuild()
        {
            if (contents == null) return;
            contents.Clear(); Direction();
            if (definition == null) return;
            foreach (string key in definition.commands)
            {
                if (key == "Control/MashBox/Viewport/PivotMode") continue;
                if(key==StudioCustomToolbars.SpacerKey){contents.Add(CreateSeparator("studio-toolbar-spacer",key,false));continue;}
                var native = StudioCustomToolbars.CreateControl(key, containerWindow as MashBoxStudioViewport);
                if (native != null) { contents.Add(native); continue; }
                string captured = key;
                var button = new EditorToolbarButton(() => Execute(captured)) { userData = key, icon = StudioCommandIcons.Get(key) };
                button.AddToClassList("unity-editor-toolbar-element");
                button.style.marginLeft = button.style.marginRight = 1;
                button.style.marginTop = button.style.marginBottom = 1;
                button.style.paddingLeft = button.style.paddingRight = 0;
                if(MenuGroupSeparators)
                {
                    contents.Add(CreateSeparator("studio-toolbar-menu-separator",key,true));
                }
                contents.Add(button);
            }
            if (definition.commands.Count == 0)
                contents.Add(new EditorToolbarButton(() => StudioToolbarCustomization.Open(containerWindow as MashBoxStudioViewport, definition.id)) { icon = StudioCommandIcons.Get("Customize/Add"), tooltip = "Add functions — customize this toolbar" });
            Direction();Refresh();
        }
        private void UpdateMenuSeparators(List<StudioModelEditor.ToolbarCommand> commands)
        {
            if(!MenuGroupSeparators||contents==null)return;
            string previous=null;
            foreach(var gap in contents.Children().Where(e=>e.name=="studio-toolbar-menu-separator"))
            {
                string group=commands?.Find(c=>c.Key==(string)gap.userData)?.MenuGroup;
                bool show=previous!=null&&group!=null&&group!=previous;
                var display=show?DisplayStyle.Flex:DisplayStyle.None;
                if(gap.style.display.value!=display)gap.style.display=display;
                if(group!=null)previous=group;
            }
        }
        private void Refresh()
        {
            if (contents == null || definition == null) return;
            Direction();
            var view = containerWindow as MashBoxStudioViewport;
            var commands = view?.Studio?.ToolbarModelEditor?.ToolbarCommands(view);
            UpdateMenuSeparators(commands);
            foreach (var element in contents.Children())
            {
                if (!(element is Button button) || !(button.userData is string key)) continue;
                var command = commands?.Find(x => x.Key == key);
                button.text = string.Empty;
                if (button is EditorToolbarButton action) action.icon = StudioCommandIcons.Get(command == null ? "Unavailable" : key);
                button.tooltip = command?.Tooltip ?? key.Substring(key.LastIndexOf('/')+1) + "\nThis function is unavailable in the current context.";
                button.SetEnabled(command != null && command.Enabled);
                button.EnableInClassList("unity-button--checked", command != null && command.Checked);
            }
        }
        internal void Execute(string key)
        {
            var view = containerWindow as MashBoxStudioViewport;
            var editor = view?.Studio?.ToolbarModelEditor;
            var command = editor?.ToolbarCommands(view).Find(x => x.Key == key);
            if (command == null || !command.Enabled || command.Execute == null) return;
            // Keep native Undo and tool actions out of retained UI dispatch/layout.
            EditorApplication.delayCall += () => {
                if (!view) return;
                var fresh = view.Studio?.ToolbarModelEditor?.ToolbarCommands(view).Find(x => x.Key == key);
                if (fresh == null || !fresh.Enabled) return;
                fresh.Execute?.Invoke(); view.MarkDirty(MashBoxStudioViewport.DirtyState.Scene); view.SyncStudioToolbarState(true);
            };
        }
    }
    [Serializable] internal sealed class StudioCustomToolbar : StudioCommandToolbar { }

    public sealed partial class MashBoxStudioViewport
    {
        private void SyncCustomToolbarDefinitions()
        {
            var definitions = StudioCustomToolbars.Read();
            foreach (var old in overlayCanvas.overlays.OfType<StudioCustomToolbar>().ToArray())
                if (!definitions.Exists(x => x.id == old.id)) { modeHiddenToolbars.Remove(old); overlayCanvas.Remove(old); }
            foreach (var definition in definitions)
            {
                var overlay = overlayCanvas.overlays.OfType<StudioCustomToolbar>().FirstOrDefault(x => x.id == definition.id);
                if (overlay == null)
                {
                    overlay = new StudioCustomToolbar(); overlay.Configure(definition);
                    overlayCanvas.Add(overlay);
                    overlayCanvas.RestoreOverlay(overlay, JsonUtility.FromJson<SaveData>("{\"containerId\":\"overlay-toolbar__top\",\"dockPosition\":0,\"index\":1000,\"displayed\":true,\"layout\":1}"));
                    overlay.displayed = true;
                }
                else overlay.Configure(definition);
            }
        }
        internal void ReloadCustomToolbars()
        {
            if (IsEmbeddedView) return;
            SyncCustomToolbarDefinitions(); InitializeStudioToolbars();
            QueueStoredToolbarSave();
            MarkDirty(DirtyState.UI);
        }
    }
}
