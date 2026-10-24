using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MashBoxSDK.AnimationStudio
{
    public sealed partial class AnimationStudioWindow
    {
        public static event Action<AnimationTake, Animator> ActorAttachmentToolbarGUI;
        public static void DrawAttachmentChoice(string label, int selected, string[] choices, Action<int> choose, Action toggle = null, bool visible = false)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(4);
                var content = new GUIContent(choices[Mathf.Clamp(selected, 0, choices.Length - 1)]);
                var style = StudioTheme.Styles.overlayDropdown;
                var rect = GUILayoutUtility.GetRect(content, style, GUILayout.Width(110), GUILayout.Height(22));
                bool open = EditorGUI.DropdownButton(rect, content, FocusType.Passive, style);
                if (DrawAttachmentRowIcon(label, visible, toggle != null)) toggle?.Invoke();
                if (!open) return;
                var menu = new StudioMenu();
                for (int i = 0; i < choices.Length; i++)
                {
                    int index = i;
                    menu.AddItem(new GUIContent(choices[i]), selected == i, () => { choose(index); SceneView.RepaintAll(); });
                }
                menu.DropDown(rect);
            }
        }
        private static bool DrawAttachmentRowIcon(string label, bool visible, bool available = true)
        {
            var rect = GUILayoutUtility.GetRect(24, 22, GUILayout.Width(24), GUILayout.Height(22));
            string tooltip = available ? (visible ? "Hide " : "Show ") + label.ToLowerInvariant() : "Choose a " + label.ToLowerInvariant() + " first";
            bool clicked;
            using (new EditorGUI.DisabledScope(!available))
                clicked = GUI.Toggle(rect, visible, new GUIContent("", tooltip), StudioTheme.Styles.overlayButton) != visible;
            if (label == "Weapon")
            {
                if (Event.current.type == EventType.Repaint)
                {
                    var color = new Color(.72f, .78f, .78f);
                    EditorGUI.DrawRect(new Rect(rect.x + 3, rect.y + 5, 18, 4), color);
                    EditorGUI.DrawRect(new Rect(rect.x + 5, rect.y + 9, 10, 3), color);
                    EditorGUI.DrawRect(new Rect(rect.x + 5, rect.y + 12, 5, 6), color);
                }
            }
            else GUI.Label(rect, new GUIContent(EditorGUIUtility.IconContent(label == "Character" ? "Avatar Icon" : "WheelCollider Icon").image, tooltip));
            return clicked;
        }
        private void WithCharacterAppearance(Action action)
        {
            int previous = activeActor;
            if (OutfitRig == null || !OutfitRig.Animator || !OutfitRig.Animator.isHuman) activeActor = -1;
            try { action(); }
            finally { activeActor = previous < actorPreviews.Count ? previous : -1; }
        }
        private void DrawCharacterChoice()
        {
            if (!defaultCharacter) defaultCharacter = AssetDatabase.LoadAssetAtPath<StudioDefaultCharacter>(DefaultCharacterPath);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(4);
                bool applied = HasDefaultCharacterAppearance();
                var current = OutfitActor;
                bool hasAppearance = current ? current.body || (current.clothing != null && current.clothing.Length > 0) : body || (clothing != null && clothing.Length > 0);
                string label = applied ? (AssetDatabase.GetAssetPath(defaultCharacter) == DefaultCharacterPath ? "Default" : defaultCharacter.name) : hasAppearance ? "Custom" : "None";
                var content = new GUIContent(label, "Character appearance on the current skeleton");
                var rect = GUILayoutUtility.GetRect(content, StudioTheme.Styles.overlayDropdown, GUILayout.Width(110), GUILayout.Height(22));
                bool open = EditorGUI.DropdownButton(rect, content, FocusType.Passive, StudioTheme.Styles.overlayDropdown);
                if (DrawAttachmentRowIcon("Character", hasAppearance, defaultCharacter && OutfitRig != null && OutfitRig.Animator))
                    Guard(() => ToggleCharacterRow(hasAppearance));
                if (!open) return;
                var menu = new StudioMenu();
                menu.AddItem(new GUIContent("None"), !hasAppearance, () => Guard(() => WithCharacterAppearance(() => ToggleDefaultCharacterAppearance(false))));
                var standard = AssetDatabase.LoadAssetAtPath<StudioDefaultCharacter>(DefaultCharacterPath);
                Action<StudioDefaultCharacter> choose = preset => Guard(() => WithCharacterAppearance(() =>
                {
                    var previous = defaultCharacter; defaultCharacter = preset;
                    try { ToggleDefaultCharacterAppearance(true); }
                    catch { defaultCharacter = previous; throw; }
                }));
                if (standard) menu.AddItem(new GUIContent("Default"), applied && defaultCharacter == standard, () => choose(standard));
                foreach (var guid in AssetDatabase.FindAssets("t:StudioDefaultCharacter"))
                {
                    var preset = AssetDatabase.LoadAssetAtPath<StudioDefaultCharacter>(AssetDatabase.GUIDToAssetPath(guid));
                    if (!preset || preset == standard) continue;
                    menu.AddItem(new GUIContent(preset.name.Replace('/', '∕')), applied && defaultCharacter == preset, () => choose(preset));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Edit character / outfit…"), false, () =>
                { if (OutfitRig == null || !OutfitRig.Animator) activeActor = -1; inspectorTab = 2; inspectorExpanded = true; viewport?.SetInspectorExpanded(true); });
                menu.DropDown(rect);
            }
        }
        [Serializable] private sealed class HiddenCharacterAppearance
        {
            public AnimationTake actor;
            public GameObject body;
            public GameObject[] clothing;
            public OutfitOptions[] options;
        }
        [SerializeField] private List<HiddenCharacterAppearance> hiddenCharacterAppearances = new List<HiddenCharacterAppearance>();
        private void ToggleCharacterRow(bool visible)
        {
            var actor = OutfitActor;
            if (visible)
            {
                var saved = new HiddenCharacterAppearance { actor = actor, body = actor ? actor.body : body,
                    clothing = actor ? actor.clothing : clothing, options = actor ? actor.outfitOptions : outfitOptions };
                ToggleDefaultCharacterAppearance(false);
                hiddenCharacterAppearances.RemoveAll(a => a.actor == actor);
                hiddenCharacterAppearances.Add(saved);
                return;
            }
            var appearance = hiddenCharacterAppearances.Find(a => a.actor == actor);
            if (appearance == null) { ToggleDefaultCharacterAppearance(true); return; }
            var preset = CreateInstance<StudioDefaultCharacter>();
            var previous = defaultCharacter;
            try
            {
                preset.body = appearance.body;
                preset.outfit = new StudioDefaultCharacter.OutfitPiece[appearance.clothing?.Length ?? 0];
                for (int i = 0; i < preset.outfit.Length; i++)
                    preset.outfit[i] = new StudioDefaultCharacter.OutfitPiece { enabled = true, prefab = appearance.clothing[i],
                        options = appearance.options != null && i < appearance.options.Length ? appearance.options[i] : new OutfitOptions { scale = 1 } };
                defaultCharacter = preset;
                ToggleDefaultCharacterAppearance(true);
                hiddenCharacterAppearances.Remove(appearance);
            }
            finally { defaultCharacter = previous; DestroyImmediate(preset); }
        }
        private void DrawAttachmentRows()
        {
            using (new GUILayout.VerticalScope(GUILayout.Width(146)))
            {
                GUILayout.Space(4);
                WithCharacterAppearance(DrawCharacterChoice);
                GUILayout.Space(4);
                if (!take || rig == null) return;
                EnsureActors();
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Space(4);
                    var visible = actorPreviews.Find(a => !IsAttachmentHidden(a.Take) && (!a.Rig.Animator || !a.Rig.Animator.isHuman));
                    var content = new GUIContent(visible == null ? "None" : visible.Take.name);
                    var rect = GUILayoutUtility.GetRect(content, StudioTheme.Styles.overlayDropdown, GUILayout.Width(110), GUILayout.Height(22));
                    bool open = EditorGUI.DropdownButton(rect, content, FocusType.Passive, StudioTheme.Styles.overlayDropdown);
                    var hidden = actorPreviews.Find(a => IsAttachmentHidden(a.Take) && (!a.Rig.Animator || !a.Rig.Animator.isHuman));
                    if (DrawAttachmentRowIcon("Vehicle", visible != null, visible != null || hidden != null))
                    {
                        if (visible != null) { lastVisibleVehicle = visible.Take; SelectQuickVehicle(null); }
                        else SelectQuickVehicle(lastVisibleVehicle && actorPreviews.Exists(a => a.Take == lastVisibleVehicle) ? lastVisibleVehicle : hidden.Take);
                    }
                    if (open)
                    {
                        var menu = new StudioMenu();
                        menu.AddItem(new GUIContent("None"), visible == null, () => SelectQuickVehicle(null));
                        foreach (string type in new[] { "BMX", "MTB", "Scooter" })
                            menu.AddItem(new GUIContent(type), visible != null && visible.Take.name == type, () => Guard(() => { AddQuickVehicle(type); SelectQuickVehicle(OutfitActor); }));
                        foreach (var actor in actorPreviews)
                        {
                            if (actor.Rig.Animator && actor.Rig.Animator.isHuman) continue;
                            var selected = actor.Take;
                            menu.AddItem(new GUIContent("Existing/" + selected.name), !IsAttachmentHidden(selected), () => SelectQuickVehicle(selected));
                        }
                        menu.AddSeparator("");
                        menu.AddItem(new GUIContent("Custom rig / preset…"), false, () => PopupWindow.Show(rect, new AttachmentPopup(this, 1)));
                        menu.DropDown(rect);
                    }
                }
                GUILayout.Space(4);
                var target = OutfitRig;
                var targetTake = OutfitActor;
                if (!target.Animator || !target.Animator.isHuman) { target = rig; targetTake = take; }
                if (ActorAttachmentToolbarGUI != null) ActorAttachmentToolbarGUI.Invoke(targetTake, target.Animator);
                else GUILayout.Label("Weapon     None", StudioTheme.Styles.miniLabel);
            }
        }
        private void SelectQuickVehicle(AnimationTake selected)
        {
            if (selected) lastVisibleVehicle = selected;
            FinishSceneEdit(); FinishActorEdits();
            foreach (var actor in actorPreviews)
            {
                if (actor.Rig.Animator && actor.Rig.Animator.isHuman) continue;
                hiddenAttachments.Remove(actor.Take);
                if (actor.Take != selected) hiddenAttachments.Add(actor.Take);
            }
            rigSelection.Clear(); ApplyAttachmentVisibility(); viewport?.Repaint();
        }
        [SerializeField] private List<AnimationTake> hiddenAttachments = new List<AnimationTake>();
        [SerializeField] private AnimationTake lastVisibleVehicle;
        private bool IsAttachmentHidden(AnimationTake actor) => hiddenAttachments.Contains(actor);
        private void ApplyAttachmentVisibility()
        {
            for (int i = 0; i < actorPreviews.Count; i++)
            {
                bool visible = !IsAttachmentHidden(actorPreviews[i].Take);
                actorPreviews[i].Rig.Root.SetActive(visible);
                if (!visible && activeActor == i) activeActor = -1;
            }
        }

        private void AddQuickVehicle(string kind)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectX/PJX Vehicles/PJX_" + kind + ".prefab");
            var skeleton = AssetDatabase.LoadAssetAtPath<GameObject>(BikePath);
            if (!source || !skeleton) throw new InvalidOperationException(kind + " defaults are unavailable in this project. Choose a custom rig or actor preset below.");
            // Reuse an existing named preview rather than creating duplicates on every click.
            EnsureActors();
            int existing = actorPreviews.FindIndex(a => a.Take.name == kind && a.Take.character == skeleton);
            if (existing >= 0)
            {
                hiddenAttachments.Remove(actorPreviews[existing].Take);
                activeActor = existing; ApplyAttachmentVisibility(); viewport?.Repaint(); return;
            }
            int previousActor = activeActor;
            AddActor(skeleton);
            var added = OutfitActor;
            try
            {
                added.name = kind;
                ReadVehicleSlots(source);
                SaveOutfitActor(); viewport?.Repaint();
            }
            catch
            {
                // A missing outfit must not leave a half-created actor in the take.
                take.actors.Remove(added); ReleaseActors(); Undo.DestroyObjectImmediate(added);
                activeActor = previousActor; EnsureActors(); Save(); throw;
            }
        }

        private sealed class AttachmentPopup : PopupWindowContent
        {
            private readonly AnimationStudioWindow owner;
            private int tab;
            private Vector2 scroll;
            internal AttachmentPopup(AnimationStudioWindow owner, int tab = 0) { this.owner = owner; this.tab = tab; }
            public override Vector2 GetWindowSize() => new Vector2(340, 390);
            public override void OnGUI(Rect rect)
            {
                using (StudioTheme.Scope()) DrawContents();
            }
            private void DrawContents()
            {
                if (!owner) { editorWindow.Close(); return; }
                GUILayout.Space(6);
                tab = GUILayout.Toolbar(tab, new[] { "Weapon", "Vehicle rig" });
                scroll = EditorGUILayout.BeginScrollView(scroll);
                if (!owner.take || owner.rig == null)
                    EditorGUILayout.HelpBox("Open an animation or take first.", MessageType.Info);
                else
                {
                    owner.EnsureActors();
                    if (tab == 0)
                    {
                        // Vehicle selection must not redirect the weapon away from the human actor.
                        var selected = owner.OutfitRig;
                        var actorTake = owner.OutfitActor;
                        if (!selected.Animator || !selected.Animator.isHuman) { selected = owner.rig; actorTake = owner.take; }
                        if (ActorInspectorGUI == null)
                            EditorGUILayout.HelpBox("No weapon attachment provider is installed in this project.", MessageType.Info);
                        else ActorInspectorGUI.Invoke(actorTake, selected.Animator);
                    }
                    else DrawVehicles();
                }
                if (!string.IsNullOrEmpty(owner.message)) EditorGUILayout.HelpBox(owner.message, MessageType.Warning);
                EditorGUILayout.EndScrollView();
            }
            private void DrawVehicles()
            {
                GUILayout.Space(8);
                EditorGUILayout.LabelField("Add vehicle rig", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                    foreach (string kind in new[] { "BMX", "MTB", "Scooter" })
                        if (GUILayout.Button(kind, GUILayout.Height(26))) owner.Guard(() => owner.AddQuickVehicle(kind));
                GUILayout.Space(8);
                owner.actorSource = (GameObject)EditorGUILayout.ObjectField("Custom rig", owner.actorSource, typeof(GameObject), false);
                using (new EditorGUI.DisabledScope(!owner.actorSource))
                    if (GUILayout.Button("Add custom rig")) owner.Guard(() => owner.AddActor(owner.actorSource));
                owner.DrawActorPresets();
                GUILayout.Space(8);
                EditorGUILayout.LabelField("Visible rigs", EditorStyles.boldLabel);
                foreach (var actor in owner.actorPreviews)
                {
                    bool visible = !owner.IsAttachmentHidden(actor.Take);
                    bool next = EditorGUILayout.ToggleLeft(actor.Take.name, visible);
                    if (next == visible) continue;
                    owner.FinishSceneEdit(); owner.FinishActorEdits();
                    if (next) owner.hiddenAttachments.Remove(actor.Take); else owner.hiddenAttachments.Add(actor.Take);
                    owner.rigSelection.Clear(); owner.ApplyAttachmentVisibility(); owner.viewport?.Repaint();
                }
                EditorGUILayout.HelpBox("Visibility only. Hidden rigs keep their animation keys and still export with the take.", MessageType.None);
            }
        }
    }
}
