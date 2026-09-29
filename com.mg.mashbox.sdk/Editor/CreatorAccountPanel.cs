#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MashBoxSDK.ContentTools.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace MashBoxSDK.SDKMain
{
    // Tokens live for the editor session only. No service keys or client secrets ship in the SDK.
    internal sealed class CreatorAccountPanel : IDisposable
    {
        internal const string Service = "https://mash-creator-accounts.azurewebsites.net/api/";
        const string OwnerTokenKey = "Mash.Creator.OwnerToken";
        static readonly string[] Kinds = { "creator", "influencer", "studio" };
        static readonly string[] Slots = { "logo", "wordmark", "banner" };
        readonly Action repaint;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        string email = "", code = "", status = "", deviceMessage = "";
        string creatorSession, nextCursor = "";
        CancellationTokenSource ownerCancellation;
        bool busy, loaded, registration, ownerLoaded, autoLoadAttempted;
        readonly Dictionary<string, string> previewErrors = new Dictionary<string, string>();
        Vector2 scroll;
        Profile draft = new Profile();
        Account account;
        readonly List<Account> accounts = new List<Account>();
        readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>();
        string OwnerToken { get => SessionState.GetString(OwnerTokenKey, ""); set => SessionState.SetString(OwnerTokenKey, value); }

        [Serializable] internal sealed class Profile
        {
            public string displayName = "", description = "", kind = "creator", accentColor = "#FF5500", backgroundColor = "#101010", backgroundStyle = "image";
            public string[] kinds = { "creator" };
            public bool published;
        }
        [Serializable] internal sealed class Partnership { public string status, requestedAt, reviewedAt; }
        [Serializable] internal sealed class Flags { public bool partner, featured, disabled; }
        [Serializable] internal sealed class Artwork
        {
            public string logo, wordmark, banner;
            public string Get(string slot) => slot == "logo" ? logo : slot == "wordmark" ? wordmark : banner;
        }
        [Serializable] internal sealed class Account
        {
            public string id, modioUserId, modioUsername, etag, updatedAt;
            public Profile profile;
            public Flags flags;
            public Artwork assets;
            public Partnership partnership;
        }
        [Serializable] sealed class Page { public Account[] items; public string cursor; }
        [Serializable] sealed class Config { public string tenantId, ownerClientId; }
        [Serializable] sealed class Error { public string error; }
        [Serializable] sealed class Device
        {
            public string device_code, user_code, verification_uri, message, error, access_token;
            public int interval, expires_in;
        }
        sealed class ApiException : Exception
        {
            public readonly long Code;
            public ApiException(long code, string message) : base(message) { Code = code; }
        }

        public CreatorAccountPanel(Action repaint)
        {
            this.repaint = repaint;
            if (!EditorPrefs.HasKey("ModIo.CurrentGame")) EditorPrefs.SetString("ModIo.CurrentGame", "ProjectX");
        }

        public void Draw()
        {
            if (creatorSession != ModIoAuth.CurrentToken)
            {
                creatorSession = ModIoAuth.CurrentToken;
                loaded = registration = autoLoadAttempted = false;
                account = null;
                draft = new Profile();
                ClearPreviews();
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Label("Mash Creator Account", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Your Mash identity and page artwork live here. Publish your game content through mod.io using Content Tools or Map Tools.", MessageType.Info);
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
            using (new EditorGUI.DisabledScope(busy))
            {
                if (!string.Equals(EditorPrefs.GetString("ModIo.CurrentGame", "ProjectX"), "ProjectX", StringComparison.OrdinalIgnoreCase))
                {
                    EditorGUILayout.HelpBox("Creator pages currently use your Project X publishing account.", MessageType.Info);
                    if (GUILayout.Button("Use Project X publishing account"))
                    {
                        EditorPrefs.SetString("ModIo.CurrentGame", "ProjectX");
                        EditorPrefs.SetString("ModIo.ApiBase", "https://g-12806.modapi.io/v1");
                    }
                }
                else if (!ModIoAuth.IsAuthorizedForCurrentGame()) DrawLogin();
                else
                {
                    if (!loaded && !registration)
                    {
                        if (!autoLoadAttempted && !busy)
                        {
                            autoLoadAttempted = true;
                            Run(LoadAccount);
                        }
                        if (GUILayout.Button("Load my Mash account")) Run(LoadAccount);
                    }
                    else DrawProfile();
                    if (GUILayout.Button("Sign out of creator / mod.io account"))
                    {
                        ModIoAuth.ClearForCurrentGame();
                        account = null; loaded = registration = false;
                        ClearPreviews();
                    }
                }
            }
#if MashBoxDev
            GUILayout.Space(24);
            GUILayout.Label("Owner management", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Only the Microsoft account authorized by Mash can approve partners, feature pages, or suspend accounts. These permissions are checked by the server.", MessageType.None);
            if (!string.IsNullOrEmpty(deviceMessage))
            {
                EditorGUILayout.HelpBox(deviceMessage, MessageType.Info);
                if (GUILayout.Button("Open Microsoft sign-in")) Application.OpenURL("https://microsoft.com/devicelogin");
                if (GUILayout.Button("Cancel owner sign-in")) ownerCancellation?.Cancel();
            }
            using (new EditorGUI.DisabledScope(busy))
            {
                if (string.IsNullOrEmpty(OwnerToken))
                {
                    if (GUILayout.Button("Sign in as owner with Microsoft")) Run(SignInOwner);
                }
                else
                {
                    if (GUILayout.Button("Refresh accounts")) Run(() => LoadOwners(false));
                    if (ownerLoaded && accounts.Count == 0) GUILayout.Label("No Mash creator accounts registered yet.");
                    foreach (Account item in accounts) DrawOwnerAccount(item);
                    if (!string.IsNullOrEmpty(nextCursor) && GUILayout.Button("Load more accounts")) Run(() => LoadOwners(true));
                    if (GUILayout.Button("Sign out of owner account"))
                    {
                        OwnerToken = ""; accounts.Clear(); ownerLoaded = false; ClearPreviews();
                    }
                }
            }
#endif
            EditorGUILayout.EndScrollView();
        }

        void DrawLogin()
        {
            GUILayout.Label("Sign in or register", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Sign in with the mod.io account you publish with, then create your Mash profile. A new mod.io account can also be registered on mod.io.", MessageType.None);
            email = EditorGUILayout.TextField("mod.io email", email);
            if (GUILayout.Button("Send login code")) ModIoAuth.BeginEmailRequest(email.Trim(), AuthStatus);
            code = EditorGUILayout.TextField("Email code", code);
            if (GUILayout.Button("Verify code")) ModIoAuth.ExchangeCode(email.Trim(), code.Trim(), AuthStatus);
            if (GUILayout.Button("Open mod.io registration")) Application.OpenURL("https://mod.io/");
        }

        void AuthStatus(string message) { if (!lifetime.IsCancellationRequested) { status = message; repaint(); } }

        void DrawProfile()
        {
            if (account != null)
            {
                EditorGUILayout.LabelField("Mash ID", account.id);
                EditorGUILayout.LabelField("Linked mod.io account", account.modioUsername + " (#" + account.modioUserId + ")");
                EditorGUILayout.LabelField("Status", account.flags.disabled ? "Suspended" : account.flags.partner ? "Partner" : "Active account");
                EditorGUILayout.LabelField("Featured page", !account.flags.partner ? "Partners only" : account.flags.featured ? "Enabled by Mash" : "Partner access enabled; placement not enabled");
            }
            if (account != null)
            {
                EditorGUILayout.LabelField("Saved page status", PageStatus(account));
                EditorGUILayout.HelpBox(PageGuidance(account), account.flags.partner && !account.profile.published ? MessageType.Warning : MessageType.Info);
            }
            GUILayout.Label("Artwork requirements", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Logo: 512 x 512 pixels (square)\nWordmark: 1536 x 512 pixels (3:1)\nBanner: 2560 x 1440 pixels (16:9 QHD)\nStatic PNG or JPEG, up to 8 MiB. Dimensions must match exactly. Use PNG to preserve transparency. Wordmark and banner uploads require partner access.", MessageType.Info);
            DrawPartnership();
            using (new EditorGUI.DisabledScope(account != null && account.flags.disabled))
            {
                draft.displayName = EditorGUILayout.TextField("Display name", draft.displayName);
                EditorGUILayout.LabelField("Description (up to 1,200 characters)");
                draft.description = EditorGUILayout.TextArea(draft.description, GUILayout.MinHeight(70));
                var selectedKinds = new List<string>(draft.kinds ?? new[] { draft.kind });
                EditorGUILayout.LabelField("Account types (choose one or more)", EditorStyles.boldLabel);
                foreach (string kind in Kinds)
                {
                    bool selected = selectedKinds.Contains(kind);
                    bool chosen = EditorGUILayout.ToggleLeft(char.ToUpperInvariant(kind[0]) + kind.Substring(1), selected);
                    if (chosen && !selected) selectedKinds.Add(kind);
                    if (!chosen && selected && selectedKinds.Count > 1) selectedKinds.Remove(kind);
                }
                draft.kinds = selectedKinds.ToArray();
                draft.kind = draft.kinds[0];
                if (account != null && account.flags.partner)
                {
                GUILayout.Label("Featured page", EditorStyles.boldLabel);
                draft.accentColor = ColorField("Accent color", draft.accentColor);
                draft.backgroundColor = ColorField("Background color", draft.backgroundColor);
                draft.backgroundStyle = EditorGUILayout.Popup("Background", draft.backgroundStyle == "image" ? 1 : 0, new[] { "Solid", "Banner image" }) == 1 ? "image" : "solid";
                draft.published = EditorGUILayout.Toggle("Publish page", draft.published);
                EditorGUILayout.HelpBox("Enable Publish page and click Save profile to publish. Uploading artwork alone does not publish your page.", MessageType.None);
                if (draft.published != account.profile.published)
                    EditorGUILayout.HelpBox("Publishing change not saved. Click Save profile to apply it.", MessageType.Warning);
                }
                if (GUILayout.Button(registration ? "Create Mash creator account" : "Save profile")) Run(SaveProfile);
                if (!registration)
                {
                    EditorGUILayout.HelpBox("Upload an account logo. Partners can also upload a wordmark and banner for their featured page. Exact sizes: logo 512 x 512; wordmark 1536 x 512 (3:1); banner 2560 x 1440 (16:9). Static PNG/JPEG, up to 8 MiB. Use transparent PNG for logos and wordmarks.", MessageType.None);
                    foreach (string slot in Slots) DrawArtwork(slot);
                    if (GUILayout.Button("Reload saved profile")) Run(LoadAccount);
                }
            }
        }

        static string PageStatus(Account item)
        {
            if (item.flags.disabled) return "Hidden - account suspended";
            if (!item.flags.partner) return "Unavailable - partnership required";
            if (!item.profile.published) return "Approved partner, but still a draft";
            if (!item.flags.featured) return "Published by creator - awaiting featured placement";
            return "Live in the community directory";
        }

        static string PageGuidance(Account item, bool owner = false)
        {
            if (item.flags.disabled) return "Suspended accounts are hidden from the directory. Saved artwork is retained.";
            if (!item.flags.partner) return "Partnership approval is required before this account can publish a featured page.";
            if (!item.profile.published)
                return owner ? "This creator has not published their page. They must reload their Creator Account, enable Publish page, then click Save profile. Approving partnership or uploading artwork does not publish it."
                    : "Your partnership is approved, but your page is not public. Enable Publish page below, then click Save profile. Uploading artwork does not publish the page.";
            if (!item.flags.featured) return "The creator has published their page. Mash must enable Featured community page and save permissions before it appears in the directory.";
            return "This page is public. The game's creator directory can take up to five minutes to refresh.";
        }

        void DrawPartnership()
        {
            if (account == null) return;
            string state = account.flags.partner ? "approved" : account.partnership?.status ?? "none";
            EditorGUILayout.LabelField("Partnership", state == "none" ? "Not requested" : char.ToUpperInvariant(state[0]) + state.Substring(1));
            if (!account.flags.partner && state != "pending")
            {
                using (new EditorGUI.DisabledScope(account.flags.disabled))
                    if (GUILayout.Button(state == "declined" || state == "revoked" ? "Request partnership again" : "Request partnership")) Run(async () =>
                    {
                        Profile edits = draft;
                        SetAccount(await Send("creator-account/partnership", "POST", creatorSession, null, account.etag));
                        draft = edits;
                        status = "Partnership requested. Awaiting owner review.";
                    });
            }
        }

        void DrawArtwork(string slot)
        {
            string path = "creator-account/assets/" + slot;
            Vector2Int required = ArtworkSize(slot);
            bool uploaded = !string.IsNullOrEmpty(account?.assets?.Get(slot));

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(char.ToUpperInvariant(slot[0]) + slot.Substring(1), EditorStyles.boldLabel, GUILayout.Width(90));
            GUILayout.Label(uploaded ? "Uploaded - saved to your account" : "No artwork uploaded", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            GUILayout.Label("Required: " + required.x + " x " + required.y + " pixels", EditorStyles.boldLabel);
            if (slot != "logo" && !account.flags.partner)
                GUILayout.Label("Partner access required to upload this artwork.", EditorStyles.wordWrappedLabel);
            if (uploaded)
            {
                if (previews.TryGetValue(path, out Texture2D image) && image != null)
                {
                    if (image.width != required.x || image.height != required.y)
                        EditorGUILayout.HelpBox("Previously uploaded artwork has a legacy size. Replace it with the required dimensions.", MessageType.Warning);
                    DrawPreview(path);
                    GUILayout.Label(image.width + " x " + image.height + " pixels", EditorStyles.miniLabel);
                }
                else if (previewErrors.TryGetValue(path, out string error))
                    EditorGUILayout.HelpBox("Upload is saved, but the preview could not load. " + error, MessageType.Warning);
                else GUILayout.Label("Loading saved artwork...", EditorStyles.miniLabel);
            }
            EditorGUILayout.BeginHorizontal();
            bool canEdit = slot == "logo" || account.flags.partner;
            using (new EditorGUI.DisabledScope(!canEdit))
                if (GUILayout.Button(uploaded ? "Replace " + slot : "Upload " + slot)) Upload(slot);
            using (new EditorGUI.DisabledScope(!uploaded))
            {
                if (GUILayout.Button("Refresh preview")) Run(() => LoadArtwork(slot));
                using (new EditorGUI.DisabledScope(!canEdit))
                    if (GUILayout.Button("Remove")) Run(() => Remove(slot));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        async Task LoadArtwork(string slot)
        {
            string path = "creator-account/assets/" + slot;
            previewErrors.Remove(path);
            if (previews.TryGetValue(path, out Texture2D old)) UnityEngine.Object.DestroyImmediate(old);
            previews.Remove(path);
            if (string.IsNullOrEmpty(account?.assets?.Get(slot))) return;
            try { await Preview(path, creatorSession); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { previewErrors[path] = ex.Message; }
            if (!lifetime.IsCancellationRequested) repaint();
        }

        async Task LoadArtwork()
        {
            // Each slot handles its own failure so a missing preview cannot hide other artwork.
            await Task.WhenAll(LoadArtwork("logo"), LoadArtwork("wordmark"), LoadArtwork("banner"));
        }

        static string ColorField(string label, string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return "#" + ColorUtility.ToHtmlStringRGB(EditorGUILayout.ColorField(label, color));
        }

        void DrawOwnerAccount(Account item)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(item.profile.displayName + " - " + item.id, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("mod.io", item.modioUsername + " (#" + item.modioUserId + ")");
            GUILayout.Label(item.profile.description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Account types", string.Join(", ", item.profile.kinds ?? new[] { item.profile.kind }));
            EditorGUILayout.LabelField("Page status", PageStatus(item));
            EditorGUILayout.HelpBox(PageGuidance(item, true), item.flags.partner && !item.profile.published ? MessageType.Warning : MessageType.Info);
            item.flags.partner = EditorGUILayout.Toggle("Partner access", item.flags.partner);
            if (!item.flags.partner) item.flags.featured = false;
            using (new EditorGUI.DisabledScope(!item.flags.partner))
                item.flags.featured = EditorGUILayout.Toggle("Featured community page", item.flags.featured);
            EditorGUILayout.LabelField("Partnership request", item.partnership?.status ?? "none");
            if (item.partnership?.status == "pending")
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Approve partnership")) Run(() => ReviewPartnership(item, true));
                if (GUILayout.Button("Decline request")) Run(() => ReviewPartnership(item, false));
                EditorGUILayout.EndHorizontal();
            }
            item.flags.disabled = EditorGUILayout.Toggle("Suspend account", item.flags.disabled);
            if (GUILayout.Button("Save permissions")) Run(async () =>
            {
                string json = await Send("owner/creators/" + item.id, "PUT", OwnerToken, JsonUtility.ToJson(item.flags), item.etag);
                Account updated = JsonUtility.FromJson<Account>(json);
                item.etag = updated.etag; item.flags = updated.flags; item.partnership = updated.partnership;
                status = "Permissions saved.";
            });
            foreach (string slot in Slots)
            {
                string path = "owner/creators/" + item.id + "/assets/" + slot;
                if (GUILayout.Button("Inspect " + slot)) Run(() => Preview(path, OwnerToken));
                DrawPreview(path);
            }
            EditorGUILayout.EndVertical();
        }

        [Serializable] sealed class PartnerDecision
        {
            public bool partner, featured, disabled;
            public string partnershipDecision;
        }

        async Task ReviewPartnership(Account item, bool approve)
        {
            var decision = new PartnerDecision { partner = approve, featured = approve && item.flags.featured,
                disabled = item.flags.disabled, partnershipDecision = approve ? "approve" : "decline" };
            string json = await Send("owner/creators/" + item.id, "PUT", OwnerToken, JsonUtility.ToJson(decision), item.etag);
            Account updated = JsonUtility.FromJson<Account>(json);
            item.etag = updated.etag; item.flags = updated.flags; item.partnership = updated.partnership;
            status = approve ? "Partnership approved. Featured page tools unlocked." : "Partnership request declined.";
        }

        async Task LoadAccount()
        {
            try
            {
                SetAccount(await Send("creator-account", "GET", creatorSession));
                status = "Mash account loaded.";
                await LoadArtwork();
            }
            catch (ApiException ex) when (ex.Code == 404)
            {
                account = null; registration = true; loaded = false;
                status = "Signed in. Set up your Mash creator profile below.";
            }
        }

        async Task SaveProfile()
        {
            SetAccount(await Send("creator-account", registration ? "POST" : "PUT", creatorSession, JsonUtility.ToJson(draft), account?.etag));
            status = "Mash profile saved. Featured placement is controlled by the owner.";
            await LoadArtwork();
        }

        void SetAccount(string json)
        {
            account = JsonUtility.FromJson<Account>(json);
            draft = JsonUtility.FromJson<Profile>(JsonUtility.ToJson(account.profile));
            if (draft.kinds == null || draft.kinds.Length == 0) draft.kinds = new[] { draft.kind ?? "creator" };
            loaded = true; registration = false;
        }

        static Vector2Int ArtworkSize(string slot) => slot == "logo" ? new Vector2Int(512, 512)
            : slot == "wordmark" ? new Vector2Int(1536, 512) : new Vector2Int(2560, 1440);

        void Upload(string slot)
        {
            string path = EditorUtility.OpenFilePanel("Upload " + slot, "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path)) return;
            Run(async () =>
            {
                if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new Exception("Artwork must be at most 8 MiB.");
                byte[] data = File.ReadAllBytes(path);
                var check = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    if (!check.LoadImage(data)) throw new Exception("Use a valid PNG or JPEG image.");
                    Vector2Int required = ArtworkSize(slot);
                    if (check.width != required.x || check.height != required.y)
                        throw new Exception(slot + " must be exactly " + required.x + " x " + required.y + " pixels. Selected image: " + check.width + " x " + check.height + ".");
                }
                finally { UnityEngine.Object.DestroyImmediate(check); }
                // Preserve unsaved text/color edits when updating an independent artwork slot.
                Profile edits = draft;
                SetAccount(await Send("creator-account/assets/" + slot, "PUT", creatorSession, null, account.etag, data));
                draft = edits;
                await LoadArtwork(slot);
                status = char.ToUpperInvariant(slot[0]) + slot.Substring(1) + " uploaded and saved to your account.";
            });
        }

        async Task Remove(string slot)
        {
            Profile edits = draft;
            SetAccount(await Send("creator-account/assets/" + slot, "DELETE", creatorSession, null, account.etag));
            draft = edits;
            string path = "creator-account/assets/" + slot;
            if (previews.TryGetValue(path, out Texture2D old)) UnityEngine.Object.DestroyImmediate(old);
            previews.Remove(path);
            previewErrors.Remove(path);
            status = "Artwork removed.";
        }

        async Task Preview(string path, string token)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(Service + path))
            {
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Cache-Control", "no-cache");
                await Execute(req);
                Texture2D texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                if (!texture.LoadImage(req.downloadHandler.data)) { UnityEngine.Object.DestroyImmediate(texture); throw new Exception("Artwork could not be decoded."); }
                if (previews.TryGetValue(path, out Texture2D old)) UnityEngine.Object.DestroyImmediate(old);
                previews[path] = texture;
            }
        }

        void DrawPreview(string path)
        {
            if (previews.TryGetValue(path, out Texture2D image) && image != null)
            {
                Rect rect = GUILayoutUtility.GetRect(200, 150, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.08f));
                GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit);
            }
        }

        async Task SignInOwner()
        {
            Config config = JsonUtility.FromJson<Config>(await Send("creator-config", "GET", null));
            string baseUrl = "https://login.microsoftonline.com/" + config.tenantId + "/oauth2/v2.0/";
            Device device = await IdentityPost(baseUrl + "devicecode", new Dictionary<string, string>
            {
                { "client_id", config.ownerClientId }, { "scope", "api://" + config.ownerClientId + "/manage openid profile" }
            });
            if (string.IsNullOrEmpty(device.device_code)) throw new Exception("Microsoft sign-in could not start: " + device.error);
            deviceMessage = "Open microsoft.com/devicelogin and enter: " + device.user_code + "\nSign in with your Mash owner Microsoft account.";
            repaint();
            Application.OpenURL("https://microsoft.com/devicelogin");
            DateTime deadline = DateTime.UtcNow.AddSeconds(Mathf.Clamp(device.expires_in, 60, 900));
            int interval = Mathf.Max(5, device.interval);
            ownerCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            try
            {
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(interval * 1000, ownerCancellation.Token);
                    Device result = await IdentityPost(baseUrl + "token", new Dictionary<string, string>
                    {
                        { "client_id", config.ownerClientId }, { "device_code", device.device_code },
                        { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" }
                    });
                    if (!string.IsNullOrEmpty(result.access_token))
                    {
                        OwnerToken = result.access_token;
                        try { await LoadOwners(false); }
                        catch { OwnerToken = ""; throw; }
                        status = "Signed in as Mash owner.";
                        return;
                    }
                    if (result.error == "slow_down") { interval += 5; continue; }
                    if (result.error != "authorization_pending") throw new Exception("Microsoft sign-in: " + result.error);
                }
                throw new Exception("Microsoft sign-in expired. Try again.");
            }
            finally { deviceMessage = ""; ownerCancellation?.Dispose(); ownerCancellation = null; }
        }

        async Task<Device> IdentityPost(string url, Dictionary<string, string> fields)
        {
            using (UnityWebRequest req = UnityWebRequest.Post(url, fields))
            {
                await Execute(req, true);
                return JsonUtility.FromJson<Device>(req.downloadHandler.text);
            }
        }

        async Task LoadOwners(bool more)
        {
            Page page = JsonUtility.FromJson<Page>(await Send("owner/creators" + (more ? "?cursor=" + Uri.EscapeDataString(nextCursor) : ""), "GET", OwnerToken));
            if (!more) accounts.Clear();
            accounts.AddRange(page.items ?? Array.Empty<Account>());
            nextCursor = page.cursor; ownerLoaded = true;
            status = "Creator accounts loaded.";
        }

        async Task<string> Send(string path, string method, string token, string json = null, string etag = null, byte[] bytes = null)
        {
            using (UnityWebRequest req = new UnityWebRequest(Service + path, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (json != null || bytes != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(bytes ?? Encoding.UTF8.GetBytes(json));
                    req.SetRequestHeader("Content-Type", bytes == null ? "application/json" : "application/octet-stream");
                }
                if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
                if (!string.IsNullOrEmpty(etag)) req.SetRequestHeader("If-Match", etag);
                await Execute(req);
                return req.downloadHandler.text;
            }
        }

        async Task Execute(UnityWebRequest req, bool allowHttpError = false)
        {
            req.timeout = 45;
            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            while (!op.isDone)
            {
                if (lifetime.IsCancellationRequested) { req.Abort(); lifetime.Token.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }
            lifetime.Token.ThrowIfCancellationRequested();
            if (req.result == UnityWebRequest.Result.ConnectionError) throw new Exception("Cannot reach the account service. Check your connection and try again.");
            if (!allowHttpError && req.responseCode >= 400)
            {
                string message = "Account service returned " + req.responseCode;
                try { message = JsonUtility.FromJson<Error>(req.downloadHandler.text)?.error ?? message; } catch { }
                throw new ApiException(req.responseCode, message);
            }
        }

        async void Run(Func<Task> action)
        {
            if (busy || lifetime.IsCancellationRequested) return;
            busy = true; status = "Workingâ€¦";
            try { await action(); }
            catch (OperationCanceledException) { status = "Sign-in cancelled."; }
            catch (ApiException ex)
            {
                status = ex.Message;
                if (ex.Code == 401) { OwnerToken = ""; ownerLoaded = false; }
            }
            catch (Exception ex) { status = ex.Message; }
            finally { busy = false; if (!lifetime.IsCancellationRequested) repaint(); }
        }

        void ClearPreviews() { foreach (Texture2D image in previews.Values) if (image != null) UnityEngine.Object.DestroyImmediate(image); previews.Clear(); previewErrors.Clear(); }
        public void Dispose() { lifetime.Cancel(); ClearPreviews(); }
    }
}
#endif
