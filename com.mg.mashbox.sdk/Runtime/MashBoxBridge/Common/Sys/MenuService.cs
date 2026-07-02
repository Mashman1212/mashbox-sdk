using System;
using System.Collections.Generic;
using System.Linq;
using MashBoxBridge.Common.Commands;
using MashBoxBridge.Common.Interfaces;
using UnityEditor;
using UnityEngine;

namespace MashBoxBridge.Common.Sys
{
    public static class MenuService
    {
        private static Dictionary<string, IMenu> s_menus = new Dictionary<string, IMenu>();
        private static Stack<IMenu> stack_menus = new Stack<IMenu>();

        private static Dictionary<string, IMenu> s_gameplayMenus = new Dictionary<string, IMenu>();
        private static Stack<IMenu> stack_GameplayMenus = new Stack<IMenu>();

        public static Action OnMenuOpen;
        public static Action OnMenuClose;

        public static int CurrentMenuStackSize => stack_menus.Count;
        public static int CurrentGameplayMenuStackSize => stack_GameplayMenus.Count;

        public static bool BlockUndo;

        // ---------- Helpers ----------
        private static bool IsAlive(IMenu m)
        {
            if (m == null)
                return false;

            if (m is UnityEngine.Object unityObject)
                return unityObject != null;

            return true;
        }

        private static string SafeName(IMenu m)
        {
            if (!IsAlive(m)) return "<destroyed>";
            try { return m.NameID; } catch { return "<name-exception>"; }
        }

        private static void CleanupDeadEntries()
        {
            // Prune dead entries from dictionaries
            if (s_menus.Count > 0)
            {
                var deadKeys = s_menus.Where(kv => !IsAlive(kv.Value)).Select(kv => kv.Key).ToList();
                foreach (var k in deadKeys) s_menus.Remove(k);
            }

            if (s_gameplayMenus.Count > 0)
            {
                var deadKeys = s_gameplayMenus.Where(kv => !IsAlive(kv.Value)).Select(kv => kv.Key).ToList();
                foreach (var k in deadKeys) s_gameplayMenus.Remove(k);
            }

            // Rebuild stacks keeping only alive, preserving order (bottom -> top)
            if (stack_menus != null && stack_menus.Count > 0)
            {
                var alive = stack_menus.Where(IsAlive).Reverse().ToList();
                stack_menus = new Stack<IMenu>(alive);
            }

            if (stack_GameplayMenus != null && stack_GameplayMenus.Count > 0)
            {
                var alive = stack_GameplayMenus.Where(IsAlive).Reverse().ToList();
                stack_GameplayMenus = new Stack<IMenu>(alive);
            }
        }

        private static void SafeClose(IMenu menu, string label)
        {
            if (!IsAlive(menu)) return;
            try
            {
                Debug.Log($"[MenuService] Close {label}: {SafeName(menu)}");
                menu.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MenuService] Skipped closing {label} '{SafeName(menu)}' — {ex.Message}");
            }
            finally
            {
                OnMenuClose?.Invoke();
                if (menu.NameID.ToLowerInvariant().Contains("settings"))
                {
                    AppEventsService.OnCloseSettingsMenu.Invoke();
                }
            }
        }

        private static void SafeOpen(IMenu menu, string label)
        {
            if (!IsAlive(menu)) return;
            try
            {
                OnMenuOpen?.Invoke();
                menu.Open();
                Debug.Log($"[MenuService] Open {label}: {SafeName(menu)}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MenuService] Failed to open {label} '{SafeName(menu)}' — {ex.Message}");
            }
        }

        // ---------- Registration ----------
        public static void Add(string key, IMenu value)
        {
            CleanupDeadEntries();
            if (!IsAlive(value))
            {
                Debug.LogWarning($"[MenuService] Add skipped (destroyed): {key}");
                return;
            }

            if (!s_menus.TryGetValue(key, out var existing) || !IsAlive(existing))
            {
                Debug.Log("[MenuService] Add: " + key);
                s_menus[key] = value;
                return;
            }
        }

        public static void AddGameplayMenu(string key, IMenu value)
        {
            CleanupDeadEntries();
            if (!IsAlive(value))
            {
                Debug.LogWarning($"[MenuService] AddGameplayMenu skipped (destroyed): {key}");
                return;
            }

            if (!s_gameplayMenus.TryGetValue(key, out var existing) || !IsAlive(existing))
            {
                Debug.Log("[MenuService] Add Gameplay Menu: " + key);
                s_gameplayMenus[key] = value;
                return;
            }
        }

        public static bool TryGetValue(string key, out IMenu value)
        {
            CleanupDeadEntries();
            if (s_menus.TryGetValue(key, out value))
            {
                if (!IsAlive(value))
                {
                    s_menus.Remove(key);
                    value = null;
                    return false;
                }
                return true;
            }
            return false;
        }

        public static bool TryGetValueGameplay(string key, out IMenu value)
        {
            CleanupDeadEntries();
            if (s_gameplayMenus.TryGetValue(key, out value))
            {
                if (!IsAlive(value))
                {
                    s_gameplayMenus.Remove(key);
                    value = null;
                    return false;
                }
                return true;
            }
            return false;
        }

        public static bool Remove(string key)
        {
            return Remove(key, null);
        }

        public static bool Remove(string key, IMenu value)
        {
            CleanupDeadEntries();
            bool removed = RemoveFromDictionary(s_menus, key, value) |
                           RemoveFromDictionary(s_gameplayMenus, key, value);

            if (stack_menus != null && stack_menus.Count > 0)
            {
                var alive = stack_menus.Where(menu => IsAlive(menu) && !MatchesRemoval(menu, key, value)).Reverse().ToList();
                stack_menus = new Stack<IMenu>(alive);
            }

            if (stack_GameplayMenus != null && stack_GameplayMenus.Count > 0)
            {
                var alive = stack_GameplayMenus.Where(menu => IsAlive(menu) && !MatchesRemoval(menu, key, value)).Reverse().ToList();
                stack_GameplayMenus = new Stack<IMenu>(alive);
            }

            return removed;
        }

        private static bool RemoveFromDictionary(Dictionary<string, IMenu> dictionary, string key, IMenu value)
        {
            if (!dictionary.TryGetValue(key, out var existing))
                return false;

            if (value != null && !ReferenceEquals(existing, value))
                return false;

            dictionary.Remove(key);
            return true;
        }

        private static bool MatchesRemoval(IMenu menu, string key, IMenu value)
        {
            if (value != null)
                return ReferenceEquals(menu, value);

            return string.Equals(SafeName(menu), key, StringComparison.Ordinal);
        }

        public static ICollection<string> Keys => s_menus.Keys;
        public static ICollection<IMenu> Values => s_menus.Values;
        public static int StackCount => stack_menus.Count;

        // ---------- UI Menus ----------
        public static void OpenMenu(string key)
        {
            CleanupDeadEntries();

            if (!s_menus.TryGetValue(key, out IMenu value) || !IsAlive(value))
            {
                if (s_menus.ContainsKey(key)) s_menus.Remove(key);
                Debug.LogWarning($"[MenuService] OpenMenu: missing/destroyed menu '{key}'");
                return;
            }

            if (stack_menus != null && stack_menus.Count > 0)
            {
                var top = stack_menus.Peek();
                if (!IsAlive(top))
                {
                    stack_menus.Pop();
                }
                else if (ReferenceEquals(value, top))
                {
                    // already on top
                    return;
                }
                else
                {
                    SafeClose(top, "UI");
                }
            }

            SafeOpen(value, "UI");
            stack_menus.Push(value);
            Debug.Log("[MenuService] UI stack size: " + stack_menus.Count);
        }

        public static void CloseMenu()
        {
            CleanupDeadEntries();
            if (stack_menus == null || stack_menus.Count == 0) return;

            var current = stack_menus.Peek();
            if (!IsAlive(current))
            {
                stack_menus.Pop();
            }
            else
            {
                SafeClose(current, "UI");
                stack_menus.Pop();
            }

            Debug.Log("[MenuService] UI stack size: " + stack_menus.Count);

            // Re-open the next down if still alive
            if (stack_menus != null && stack_menus.Count > 0)
            {
                var next = stack_menus.Peek();
                SafeOpen(next, "UI");
            }
        }

        public static void TryClose(IMenu menu)
        {
            if (stack_menus == null || stack_menus.Count == 0 || menu == null) return;
            CleanupDeadEntries();

            var top = stack_menus.Peek();
            if (ReferenceEquals(top, menu))
            {
                CloseMenu();
            }
        }

        // ---------- Gameplay Menus ----------
        public static void OpenMenuGameplay(string key)
        {
            Debug.Log("[MenuService] Try OpenMenuGameplay: " + key);
            CleanupDeadEntries();

            if (!s_gameplayMenus.TryGetValue(key, out IMenu value) || !IsAlive(value))
            {
                if (s_gameplayMenus.ContainsKey(key)) s_gameplayMenus.Remove(key);
                Debug.LogWarning($"[MenuService] OpenMenuGameplay: missing/destroyed menu '{key}'");
                return;
            }

            if (stack_GameplayMenus != null && stack_GameplayMenus.Count > 0)
            {
                var top = stack_GameplayMenus.Peek();
                if (!IsAlive(top))
                {
                    stack_GameplayMenus.Pop();
                }
                else if (ReferenceEquals(value, top))
                {
                    return; // same on top
                }
                else
                {
                    SafeClose(top, "Gameplay");
                }
            }

            if (stack_GameplayMenus == null) stack_GameplayMenus = new Stack<IMenu>();
            SafeOpen(value, "Gameplay");
            stack_GameplayMenus.Push(value);
            Debug.Log("[MenuService] Gameplay stack size: " + stack_GameplayMenus.Count);
        }

        public static void CloseMenuGameplay()
        {
            if (BlockUndo) return;
            CleanupDeadEntries();

            if (stack_GameplayMenus == null || stack_GameplayMenus.Count == 0) return;

            var current = stack_GameplayMenus.Peek();
            if (!IsAlive(current))
            {
                stack_GameplayMenus.Pop();
            }
            else
            {
                SafeClose(current, "Gameplay");
                stack_GameplayMenus.Pop();
            }

            Debug.Log("[MenuService] Gameplay stack size: " + stack_GameplayMenus.Count);

            if (stack_GameplayMenus != null && stack_GameplayMenus.Count > 0)
            {
                var next = stack_GameplayMenus.Peek();
                SafeOpen(next, "Gameplay");
            }
        }

        public static void TryCloseGameplay(IMenu menu)
        {
            if (BlockUndo || menu == null) return;
            CleanupDeadEntries();

            if (stack_GameplayMenus != null && stack_GameplayMenus.Count > 0)
            {
                if (ReferenceEquals(stack_GameplayMenus.Peek(), menu))
                {
                    CloseMenuGameplay();
                    Debug.Log("[MenuService] CommandSystemServiceHandler.UndoGameplayStack()");
                    CommandSystemServiceHandler.UndoGameplayStack();
                }
            }
        }

        // ---------- Reset / Force close ----------
        public static void ResetTitle()
        {
            BlockUndo = false;
            try
            {
                if (stack_GameplayMenus != null)
                    ForceCloseAll(stack_GameplayMenus, "Gameplay");

                if (stack_menus != null)
                    ForceCloseAll(stack_menus, "UI");

                s_menus?.Clear();
                s_gameplayMenus?.Clear();

                stack_GameplayMenus?.Clear();
                stack_menus?.Clear();
            }
            finally
            {
                BlockUndo = false;
            }
        }

        /// <summary>
        /// Force-closes every menu in a stack from top to bottom without reopening anything beneath.
        /// Skips destroyed menus and suppresses MissingReferenceExceptions.
        /// </summary>
        private static void ForceCloseAll(Stack<IMenu> stack, string label)
        {
            CleanupDeadEntries();
            if (stack == null || stack.Count == 0) return;

            Debug.Log($"[MenuService] ForceCloseAll({label}) — closing {stack.Count} menu(s)");

            while (stack.Count > 0)
            {
                IMenu top = null;
                try
                {
                    top = stack.Pop();
                    if (IsAlive(top))
                    {
                        Debug.Log($"[MenuService]  • Close {label} menu: {SafeName(top)}");
                        top.Close();
                    }
                    else
                    {
                        Debug.Log($"[MenuService]  • Skip close {label} menu: <destroyed>");
                    }
                    OnMenuClose?.Invoke();
                    if (top.NameID.ToLowerInvariant().Contains("settings"))
                    {
                        AppEventsService.OnCloseSettingsMenu.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MenuService] Error closing {label} menu '{SafeName(top)}': {ex.Message}");
                }
            }
        }
    }
}
