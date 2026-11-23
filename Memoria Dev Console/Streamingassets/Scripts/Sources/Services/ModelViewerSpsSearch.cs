using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Memoria.Assets;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    public sealed partial class ModelViewerService
    {
        private sealed class SpsSearchEntry
        {
            public Int32 Index, Id;
            public String Map, Category;
        }
        private Boolean _spsSearchOpen, _spsSearchFocus;
        private Boolean _spsSearchEscapePending;
        private String _spsSearchQuery = "", _spsSearchFilter = "", _spsSearchStatus = "";
        private Int32 _spsSearchPage, _spsPendingIndex = -1;
        private readonly List<SpsSearchEntry> _spsSearchEntries = new List<SpsSearchEntry>();
        private readonly List<SpsSearchEntry> _spsSearchMatches = new List<SpsSearchEntry>();
        private const Int32 SpsPageSize = 8;

        // Stable Memoria does not wrap Ctrl+arrow jumps before indexing geoList.
        // Intercept only out-of-range jumps; leave ordinary native controls alone.
        private Boolean HandleModelBoundaryJump()
        {
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) ||
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return false;
            int direction = Input.GetKeyDown(KeyCode.RightArrow) ? 1 : Input.GetKeyDown(KeyCode.LeftArrow) ? -1 : 0;
            if (direction == 0) return false;
            Type viewer = typeof(ModelViewerScene);
            foreach (string state in new[] { "InsertText", "isLoadingModel", "isLoadingWeaponModel", "isLoadingFloorModel" })
                if (System.Object.Equals(GetStaticFieldValue(viewer, state), true)) return false;
            IList models = GetStaticFieldValue(viewer, "geoList") as IList;
            if (models == null || models.Count == 0) return true;
            int current = (int)GetStaticFieldValue(viewer, "currentGeoIndex");
            int target = current + direction * 10;
            if (target >= 0 && target < models.Count) return false;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo change = viewer.GetMethod("ChangeModel", flags);
            for (int attempt = 0; attempt < models.Count; attempt++, target += direction)
            {
                int index = WrapModelIndex(target, models.Count);
                int id = (int)models[index].GetType().GetField("Id").GetValue(models[index]);
                if (id == 276 || id == 393 || id == 394) continue;
                change.Invoke(null, new System.Object[] { index });
                if (GetStaticFieldValue(viewer, "currentModel") != null) break;
            }
            return true;
        }

        private static int WrapModelIndex(int index, int count)
        {
            return count <= 0 ? -1 : (index % count + count) % count;
        }

        private Boolean HandleSpsSearch()
        {
            if (_spsPendingIndex >= 0)
            {
                Int32 index = _spsPendingIndex;
                _spsPendingIndex = -1;
                try
                {
                    Type viewer = typeof(ModelViewerScene);
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
                    viewer.GetMethod("ChangeModel", flags).Invoke(null, new System.Object[] { index });
                    FieldInfo controlled = viewer.GetField("partcontrolled", flags);
                    if (controlled != null) controlled.SetValue(null, Enum.Parse(controlled.FieldType, "MODEL"));
                    _lastFramedModel = null;
                    _frameDelay = 0;
                    if (GetStaticFieldValue(viewer, "currentModel") == null)
                    {
                        _spsSearchOpen = true;
                        _spsSearchStatus = "This SPS could not be loaded. Choose another map or effect.";
                    }
                }
                catch (Exception ex)
                {
                    _spsSearchOpen = true;
                    _spsSearchStatus = "Could not open this SPS. See Memoria.log for details.";
                    ViewerDiagnostics.Warning("[Dev Console][SPS Search] " + ex);
                }
                return true; // Do not pass the selection key through to viewer controls.
            }
            if (Input.GetKeyDown(KeyCode.F3))
            {
                _spsSearchOpen = !_spsSearchOpen;
                if (_spsSearchOpen)
                {
                    try
                    {
                        _spsSearchEntries.Clear();
                        IList models = GetStaticFieldValue(typeof(ModelViewerScene), "geoList") as IList;
                        if (models == null) throw new InvalidOperationException("Viewer catalog is not initialized");
                        Type entryType = models.Count > 0 ? models[0].GetType() : null;
                        if (entryType != null)
                        {
                            FieldInfo kind = entryType.GetField("Kind"), id = entryType.GetField("Id"), name = entryType.GetField("Name"), category = entryType.GetField("ModelCategory");
                            FieldInfo spsKind = typeof(ModelViewerScene).GetField("MODEL_KIND_SPS", BindingFlags.Static | BindingFlags.NonPublic);
                            Int32 expectedKind = (Int32)spsKind.GetRawConstantValue();
                            for (Int32 i = 0; i < models.Count; i++)
                                if ((Int32)kind.GetValue(models[i]) == expectedKind)
                                    _spsSearchEntries.Add(new SpsSearchEntry { Index = i, Id = (Int32)id.GetValue(models[i]), Map = (String)name.GetValue(models[i]), Category = category.GetValue(models[i]).ToString() });
                        }
                        _spsSearchFilter = null;
                        _spsSearchFocus = true;
                        _spsSearchStatus = "";
                    }
                    catch (Exception ex)
                    {
                        _spsSearchStatus = "Could not read the SPS catalog.";
                        ViewerDiagnostics.Warning("[Dev Console][SPS Search] " + ex);
                    }
                }
                return true;
            }
            return _spsSearchOpen;
        }

        private static Boolean MatchesSpsSearch(Int32 id, String map, String category, String query)
        {
            query = query.Trim();
            if (query.Length == 0) return true;
            Int32 wanted;
            if (Int32.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out wanted)) return id == wanted;
            return (map ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (category ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private GUISkin _spsSearchSkin;
        private Vector2 _spsSearchScroll;
        private readonly ViewerHoverHelp _spsSearchHelp = new ViewerHoverHelp();
        public void DrawSpsSearch()
        {
            if (!IsActive || !ModelViewerScene.initialized || !_spsSearchOpen) return;
            GUISkin oldSkin = GUI.skin;
            if (_spsSearchSkin == null)
            {
                _spsSearchSkin = UnityEngine.Object.Instantiate(GUI.skin);
                foreach (GUIStyle style in new[] {_spsSearchSkin.label, _spsSearchSkin.button, _spsSearchSkin.textField})
                { style.fontSize = 19; style.normal.textColor = Color.white; }
                _spsSearchSkin.button.alignment = TextAnchor.MiddleLeft;
                _spsSearchSkin.button.padding = new RectOffset(12,12,7,7);
                _spsSearchSkin.button.normal.textColor = ConsoleTheme.UnityAccent;
            }
            GUI.skin = _spsSearchSkin;
            Int32 oldDepth = GUI.depth;
            GUI.depth = -10000;
            try
            {
                Single width = Math.Min(620f, Screen.width - 20f), height = Math.Min(650f, Screen.height - 20f);
                Rect area = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
                Color beforeColor = GUI.color;
                GUI.color = new Color(.045f,.05f,.055f,1);
                GUI.DrawTexture(area,Texture2D.whiteTexture);
                GUI.color = beforeColor;
                GUI.Box(area, "");
                Event current = Event.current;
                Boolean escape = current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape;
                Boolean enter = current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter);
                if (escape || enter) current.Use();
                GUILayout.BeginArea(new Rect(area.x + 14, area.y + 12, area.width - 28, area.height - 24));
                GUILayout.Label("Find SPS — enter an exact ID or a map/category name");
                GUI.SetNextControlName("MDC_SPS_Search");
                _spsSearchQuery = GUILayout.TextField(_spsSearchQuery, 80);
                ViewerHoverHelp.Last("Search by exact SPS ID or by map/category. Duplicate IDs can refer to different maps.");
                if (_spsSearchFocus) { GUI.FocusControl("MDC_SPS_Search"); _spsSearchFocus = false; }
                if (_spsSearchFilter != _spsSearchQuery)
                {
                    _spsSearchFilter = _spsSearchQuery;
                    _spsSearchPage = 0;
                    _spsSearchMatches.Clear();
                    foreach (SpsSearchEntry entry in _spsSearchEntries)
                        if (MatchesSpsSearch(entry.Id, entry.Map, entry.Category, _spsSearchQuery)) _spsSearchMatches.Add(entry);
                }
                Int32 pages = Math.Max(1, (_spsSearchMatches.Count + SpsPageSize - 1) / SpsPageSize);
                GUILayout.Label(_spsSearchMatches.Count + " matches. IDs can repeat; choose the correct map.");
                _spsSearchScroll = GUILayout.BeginScrollView(_spsSearchScroll, GUILayout.ExpandHeight(true));
                Int32 start = _spsSearchPage * SpsPageSize;
                for (Int32 i = start; i < Math.Min(start + SpsPageSize, _spsSearchMatches.Count); i++)
                {
                    SpsSearchEntry entry = _spsSearchMatches[i];
                    if (GUILayout.Button(new GUIContent("ID " + entry.Id + "    " + entry.Map + "    [" + entry.Category + "]", "Load this SPS from its specific map into Model Viewer.")))
                        QueueSpsSelection(entry.Index);
                }
                GUILayout.EndScrollView();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Previous", "Previous page of matching SPS assets.")) && _spsSearchPage > 0) _spsSearchPage--;
                GUILayout.Label("Page " + (_spsSearchPage + 1) + " / " + pages);
                if (GUILayout.Button(new GUIContent("Next", "Next page of matching SPS assets.")) && _spsSearchPage + 1 < pages) _spsSearchPage++;
                if (GUILayout.Button(new GUIContent("Close (Esc)", "Close SPS search and return to the model."))) _spsSearchOpen = false;
                GUILayout.EndHorizontal();
                GUILayout.Label(_spsSearchStatus);
                GUILayout.Label("Enter opens a single match. F3 / Esc closes search.");
                GUILayout.EndArea();
                if (escape || enter)
                {
                    if (escape) { _spsSearchOpen = false; _spsSearchEscapePending = true; }
                    else if (_spsSearchMatches.Count == 1) QueueSpsSelection(_spsSearchMatches[0].Index);
                    else _spsSearchStatus = "Choose a result button when an ID has multiple matches.";
                    current.Use();
                }
            }
            finally { _spsSearchHelp.Draw(); GUI.skin = oldSkin; GUI.depth = oldDepth; }
        }
        private void QueueSpsSelection(Int32 index) { _spsPendingIndex = index; _spsSearchOpen = false; }
    }
}
