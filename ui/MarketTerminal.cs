using System;
using System.Collections.Generic;
using ClickThroughFix;
using KSP.UI.Screens;
using UnityEngine;

namespace OreMarket.Terminal
{
    // Separate optional assembly: this observer has no mining, persistence or Funding writes.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class MarketTerminal : MonoBehaviour
    {
        private static MarketTerminal instance;
        private readonly TerminalSession session = new TerminalSession();
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        private readonly GUI.WindowFunction drawCallback;
        private List<MarketGroup> groups = new List<MarketGroup>();
        private ApplicationLauncherButton button;
        private ApplicationLauncher launcher;
        private long marketRevision = -1, chartRevision = -1;
        private string chartIdentity;
        private GUIStyle welcomeTitle, welcomeMotto, welcomeDetail, welcomeVersion;
        private int welcomeDetailSize;
        private string welcomeMark;
        private static readonly GUIContent WelcomeTitle = new GUIContent("The Kerbol Exchange");
        private Font welcomeFont;
        private ChartRaster chartRaster;
        private MarketPeriod[] chartCandles = Array.Empty<MarketPeriod>();
        private string[] timeLabels = Array.Empty<string>();
        private int[] timeXs = Array.Empty<int>();
        private string latestLabel, hoverText, countdownText = "--:--";
        private MarketNews[] news = Array.Empty<MarketNews>();
        private int hoverIndex = int.MinValue, countdownSecond = int.MinValue;
        private GUIStyle axisText,launcherTooltip;
        private ChartMode chartMode;
        private MarketRange chartRange;
        private Texture2D chartTexture;
        private bool hasHistory, helpVisible;
        private readonly MonitorState monitor=new MonitorState();
        private bool? requestedCompact;
        private Rect fullWindow=new Rect(80,80,1040,660),compactWindow=new Rect(80,80,MonitorLayout.Width,MonitorLayout.Height);
        private GUIStyle monitorPanel;
        private Texture2D monitorBackground,hudLine,hudGrid,hudTooltipBackground,hudEventBackground,hudRuleShadow;
        private GUIStyle hudTitle,hudText,hudSmall,hudRight,hudAxis,hudTooltip,hudButton,hudButtonText;
        private string[] hudPrices=Array.Empty<string>();
        private long monitorEventRevision=-1;
        private string monitorDiagnostic;
        private MonitorEvent[] monitorEvents=Array.Empty<MonitorEvent>();
        private GUIContent[] monitorEventHints=Array.Empty<GUIContent>();
        private GUIContent monitorPrice=new GUIContent("",TerminalHelp.CurrentPrice);
        private string monitorOrigin,monitorCountdown,monitorCaption;
        private MarketEventMarker[] chartMarkers=Array.Empty<MarketEventMarker>();
        private double[] markerXs=Array.Empty<double>();
        private string[] markerHints=Array.Empty<string>();
        private string markerHint;
        private int markerHover=-1;
        private readonly Dictionary<string,GUIContent> changeTips=new Dictionary<string,GUIContent>(StringComparer.Ordinal);
        private static readonly GUIContent ObjectPriceHelp=new GUIContent("INDICATIVE / UNIT  ?","No verified sales are available for this commodity.");
        private static readonly GUIContent CurrentHelp=new GUIContent("CURRENT / UNIT  ?",TerminalHelp.CurrentPrice);
        private static readonly GUIContent BaseHelp=new GUIContent("CONFIGURED BASE / UNIT  ?",TerminalHelp.BasePrice);
        private static readonly GUIContent NewsHelp=new GUIContent("MARKET NEWS  ?",TerminalHelp.News);
        private static readonly GUIContent CandleHelp=new GUIContent("Candle",TerminalHelp.Candles);
        private static readonly GUIContent AftermathHelp=new GUIContent("AFTERMATH  ?",TerminalHelp.Aftermath);
        private static readonly GUIContent[] ModeHelp={CandleHelp,new GUIContent("Line"),new GUIContent("Area"),new GUIContent("Bar")};
        private GUIContent updateHelp=new GUIContent("",TerminalHelp.Update);
        private Texture2D icon;
        private bool iconOwned;
        private readonly Dictionary<string, Texture2D> sparkTextures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> planetIcons = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private GUISkin skin;
        private GUIStyle panel, heading, subheading, label, muted, price, row, selectedRow, tab, selectedTab, groupHeader, right, small;
        private Texture2D background, surface, border, accent, neutralLine, risingLine, fallingLine;
        private Rect window = new Rect(80, 80, 1040, 660);
        private Vector2 sidebarScroll, contentScroll;
        private bool visible, subscribed, locked, uiHidden, launcherHovered;
        private static readonly GUIContent LauncherTip=new GUIContent("The Kerbol Exchange");
        private bool focused = true;
        private float scale = 1, nextButtonAttempt;
        private int windowId, oldWidth, oldHeight;
        private float oldScale;
        private const string LockName = "OreMarket.UI.Hover";
        private static readonly string[] Modes = { "Candle", "Line", "Area", "Bar" };
        private static readonly string[] Ranges = { "15M", "1H", "6H", "24H" };

        public MarketTerminal() { drawCallback = DrawWindow; }
        public void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this; DontDestroyOnLoad(gameObject); windowId = GetInstanceID();
            GameEvents.onGUIApplicationLauncherReady.Add(EnsureButton);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveButton);
            GameEvents.onGameSceneLoadRequested.Add(SceneChanging);
            GameEvents.onHideUI.Add(HideUI);
            GameEvents.onShowUI.Add(ShowUI);
            subscribed = true;
            Debug.Log("[OreMarket.UI] The Kerbol Exchange terminal ready; stock launcher and ClickThroughBlocker");
        }
        public void Start() { EnsureButton(); }
        private void HideUI() { launcherHovered=false; uiHidden = true; helpVisible=false; ReleaseLock(); }
        private void ShowUI() { uiHidden = false; }
        public void OnApplicationFocus(bool hasFocus) { focused = hasFocus; if (!hasFocus) ReleaseLock(); }
        private static bool Supported => HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.FLIGHT ||
            HighLogic.LoadedScene == GameScenes.TRACKSTATION || HighLogic.LoadedScene == GameScenes.EDITOR;
        private void EnsureButton()
        {
            if (!Supported || !ApplicationLauncher.Ready || ApplicationLauncher.Instance == null || button != null) return;
            if (icon == null) {icon=TerminalAssets.Load(TerminalAssets.Root+"KE Toolbar");if(icon==null){icon=MakeIcon();iconOwned=true;}}
            launcher = ApplicationLauncher.Instance;
            button = launcher.AddModApplication(Open, Close, ButtonHover, ButtonHoverOut, null, null,
                ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW |
                ApplicationLauncher.AppScenes.TRACKSTATION | ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH, icon);
            button.name="The Kerbol Exchange";
        }
        private void ButtonHover(){launcherHovered=true;}
        private void ButtonHoverOut(){launcherHovered=false;}
        private void Open()
        {
            if (!Supported) return;
            RebuildCatalog(); visible = true; FitWindow();
        }
        private void Close()
        {
            visible = false; helpVisible=false; requestedCompact=null; ReleaseLock();
            if (button != null) button.SetFalse(false);
        }
        private void SceneChanging(GameScenes scene) { Close(); RemoveButton(); if (scene == GameScenes.MAINMENU) session.LeaveSave(); }
        private void RemoveButton()
        {
            Close(); launcherHovered=false;
            var previous = button; button = null;
            try { if (previous != null && launcher != null) launcher.RemoveModApplication(previous); }
            catch (Exception e) { Debug.LogWarning("[OreMarket.UI] Launcher teardown: " + e.Message); }
            launcher = null;
        }
        public void Update()
        {
            if (instance != this) return;
            if (!Supported) { if (visible) Close(); return; }
            if (button == null && Time.realtimeSinceStartup >= nextButtonAttempt)
            { nextButtonAttempt = Time.realtimeSinceStartup + 1; EnsureButton(); }
            if (!visible || uiHidden || !focused) { ReleaseLock(); return; }
            if (marketRevision != MarketScenario.Revision) RebuildCatalog();
            if (Screen.width != oldWidth || Screen.height != oldHeight || GameSettings.UI_SCALE != oldScale) FitWindow();
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool hover = window.Contains(mouse);
            if (hover && !locked)
            {
                // CTB handles editor/flight focus. This supplement covers KSC,
                // tracking-station picking and wheel camera controls in screen space.
                InputLockManager.SetControlLock(ControlTypes.KSC_FACILITIES | ControlTypes.KSC_UI | ControlTypes.MAP_UI | ControlTypes.TRACKINGSTATION_UI |
                    ControlTypes.CAMERACONTROLS | ControlTypes.EDITOR_PAD_PICK_PLACE | ControlTypes.EDITOR_PAD_PICK_COPY |
                    ControlTypes.EDITOR_ICON_PICK | ControlTypes.EDITOR_GIZMO_TOOLS, LockName);
                locked = true;
            }
            else if (!hover) ReleaseLock();
        }
        private void ReleaseLock()
        {
            if (!locked) return;
            InputLockManager.RemoveControlLock(LockName); locked = false;
        }
        private void FitWindow()
        {
            oldWidth = Screen.width; oldHeight = Screen.height; oldScale = GameSettings.UI_SCALE;
            scale = (float)MonitorLayout.FitScale(monitor.Compact,oldScale,oldWidth,oldHeight);
            window.width = (monitor.Compact?MonitorLayout.Width:1040) * scale; window.height = (monitor.Compact?MonitorLayout.Height:660) * scale;
            ClampWindow();
            if (skin != null) UpdateFonts();
        }
        private void ClampWindow()
        {
            window.x = Mathf.Clamp(window.x, 12, Mathf.Max(12, Screen.width - window.width - 12));
            window.y = Mathf.Clamp(window.y, 12, Mathf.Max(12, Screen.height - window.height - 12));
        }
        private void RebuildCatalog()
        {
            session.BeginSave(HighLogic.CurrentGame);
            var bodies = new List<MarketBody>();
            if (FlightGlobals.Bodies != null)
                foreach (CelestialBody body in FlightGlobals.Bodies)
                {
                    if (body == null) continue;
                    OrePricing.TryGetUnitPrice(body.bodyName, out double unitPrice);
                    OrePricing.TryGetBasePrice(body.bodyName, out double basePrice);
                    bodies.Add(new MarketBody { Identity = body.bodyName, Parent = body.referenceBody == null ? null : body.referenceBody.bodyName,
                        Star = body.isStar, Price = unitPrice, BasePrice = basePrice,
                        History = ChartHistory.Convert(MarketScenario.History(body.bodyName, 3600)), Live = MarketScenario.Ready });
                }
            groups = MarketCatalog.Build(bodies);ObjectSidebar.Append(groups); session.Bind(groups);
            marketRevision = MarketScenario.Revision; countdownSecond=int.MinValue;
            foreach (var group in groups)
            {
                CachePlanetIcon(group.Identity, group.Moons);
                foreach (var market in group.Markets) { CachePlanetIcon(market.Identity, null); CacheSparkline(market); changeTips[market.Identity]=new GUIContent(market.Watch.ChangeText,TerminalHelp.Change(market.Watch.Samples.Length==0?Array.Empty<MarketPeriod>():ChartHistory.Convert(MarketScenario.History(market.Identity,3600)))); }
            }
            Debug.Log("[OreMarket.UI] Catalog refreshed: groups=" + groups.Count + " selected=" + session.Selected);
        }
        public void OnGUI()
        {
            if (instance != this || !Supported || uiHidden || !focused) return;
            if(launcherHovered && button!=null) {
                if(launcherTooltip==null){launcherTooltip=new GUIStyle(GUI.skin.box){padding=new RectOffset(9,9,5,5),alignment=TextAnchor.MiddleCenter};launcherTooltip.normal.background=Tile(new Color(.06f,.06f,.06f,.94f));launcherTooltip.normal.textColor=new Color(.91f,.91f,.91f);}
                launcherTooltip.fontSize=Mathf.RoundToInt(12*Mathf.Clamp(GameSettings.UI_SCALE,.75f,1.75f));
                Vector2 size=launcherTooltip.CalcSize(LauncherTip),mouse=Event.current.mousePosition;
                GUI.Box(new Rect(Mathf.Clamp(mouse.x-size.x-12,4,Mathf.Max(4,Screen.width-size.x-4)),Mathf.Clamp(mouse.y+18,4,Mathf.Max(4,Screen.height-size.y-4)),size.x,size.y),LauncherTip,launcherTooltip);
            }
            if(!visible)return;
            GUISkin previousSkin = GUI.skin;
            Matrix4x4 previousMatrix = GUI.matrix;
            bool previousEnabled = GUI.enabled;
            Color previousColor = GUI.color, previousBackground = GUI.backgroundColor, previousContent = GUI.contentColor;
            try
            {
                if (skin == null) BuildStyles();
                GUI.skin = skin; GUI.matrix = Matrix4x4.identity;
                GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
                window = ClickThruBlocker.GUIWindow(windowId, window, drawCallback, "The Kerbol Exchange", monitor.Compact?monitorPanel:panel);
                if(requestedCompact.HasValue){bool compact=requestedCompact.Value;requestedCompact=null;SetCompact(compact);}
                ClampWindow();
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { Close(); Debug.LogError("[OreMarket.UI] Window closed safely after UI error: " + e); }
            finally { GUI.skin = previousSkin; GUI.matrix = previousMatrix; GUI.color = previousColor; GUI.backgroundColor = previousBackground; GUI.contentColor = previousContent; GUI.enabled = previousEnabled; }
        }
        private Rect R(float x, float y, float w, float h) => new Rect(x * scale, y * scale, w * scale, h * scale);
        private static void Fill(Rect rectangle, Texture2D texture) { if (Event.current.type == EventType.Repaint) GUI.DrawTexture(rectangle, texture); }
        private void DrawWindow(int id)
        {
            if(monitor.Compact){DrawCompact();return;}
            markerHint=null;
            Fill(R(0, 0, 1040, 660), background);
            Fill(R(0, 47, 281, 585), surface);
            Fill(R(0, 0, 1040, 46), surface);
            GUI.Label(R(18, 8, 400, 30), "THE KERBOL EXCHANGE", heading);
            if(GUI.Button(R(724,9,112,28),"MINIMIZE",tab)){requestedCompact=true;return;}
            if(GUI.Button(R(848,9,30,28),"?",tab))helpVisible=!helpVisible;
            if (GUI.Button(R(890, 9, 88, 28), "Refresh", tab)) RebuildCatalog();
            if (GUI.Button(R(991, 9, 32, 28), "X", tab)) { Close(); return; }
            Fill(R(0, 46, 1040, 1), border);
            bool interactive=GUI.enabled;GUI.enabled=interactive&&!helpVisible;
            DrawSidebar();
            Fill(R(281, 47, 1, 586), border);
            if (session.Welcome) DrawWelcome(); else DrawCommodity();
            GUI.enabled=interactive;
            if(helpVisible)DrawHelp();else DrawHint();
            Fill(R(0, 632, 1040, 1), border);
            GUI.Label(R(918, 638, 105, 18), TerminalHelp.DisplayVersion, small);
            GUI.DragWindow(R(0, 0, 880, 46));
        }
        private void DrawSidebar()
        {
            GUI.Label(R(18, 59, 245, 24), "MARKETS", subheading);
            GUI.Label(R(130, 62, 125, 18), "CHANGE / UP TO 1H", small);
            float countHeight = SidebarLayout.ContentHeight(groups, session.Collapsed);
            sidebarScroll = GUI.BeginScrollView(R(10, 90, 263, 530), sidebarScroll,
                new Rect(0, 0, 242 * scale, Mathf.Max(530 * scale, countHeight * scale + 10)));
            float y = 0; bool divided = false;
            foreach (var group in groups)
            {
                if(group.ObjectGroup){Fill(R(8,y+5,224,1),border);y+=12;}
                if (!group.Expandable)
                {
                    if (!divided)
                    {
                        Fill(R(8, y + 9, 224, 1), border);
                        GUI.Label(R(10, y + 15, 225, 20), "INDIVIDUAL MARKETS", small);
                        y += SidebarLayout.DividerHeight; divided = true;
                    }
                    if (group.Markets.Count > 0) DrawMarketRow(group.Markets[0], ref y);
                    continue;
                }
                bool collapsed = session.Collapsed.Contains(group.Identity);
                if (GUI.Button(R(0, y, 240, SidebarLayout.HeaderHeight - 2), "", groupHeader))
                { if (collapsed) session.Collapsed.Remove(group.Identity); else session.Collapsed.Add(group.Identity); }
                DrawPlanetIcon(group.IconKey, R(3, y + 1, 44, 44));
                GUI.Label(R(54, y + 11, 160, 24), group.Title, label);
                GUI.Label(R(221, y + 12, 16, 22), collapsed ? "+" : "-", small);
                y += SidebarLayout.HeaderHeight;
                if (!collapsed) foreach (var market in group.Markets) DrawMarketRow(market, ref y);
                y += SidebarLayout.GroupGap;
            }
            GUI.EndScrollView();
            if (groups.Count == 0) GUI.Label(R(22, 100, 235, 100), "No market quotes available.\nCheck the catalogue, Prices.cfg and KSP.log.", muted);
        }
        private void DrawMarketRow(Commodity market, ref float y)
        {
            bool chosen = market.Identity == session.Selected;
            float rowHeight=market.ObjectMarket==null?SidebarLayout.RowHeight:SidebarLayout.ObjectRowHeight,extra=market.ObjectMarket==null?0:12;
            float indent = Math.Min(3, market.Depth) * 8;
            if (GUI.Button(R(indent, y, 240 - indent, rowHeight-3), "", chosen ? selectedRow : row)) { session.Select(market); contentScroll = Vector2.zero; }
            if (chosen) Fill(R(indent, y + 6, 2, rowHeight-15), accent);
            DrawPlanetIcon(market.IconKey, market.ObjectMarket==null?R(indent+5,y+4,23,23):R(indent+3,y+3,26,26));
            GUI.Label(R(indent + 31, y + 4, 196 - indent, 22), market.RowTitle, market.ObjectMarket==null?label:small);
            if(market.ObjectMarket!=null)GUI.Label(R(indent+31,y+22,115-indent,14),market.Secondary,small);
            DrawSparkline(market, R(indent + 12, y + 33+extra, 65, 12));
            Color oldText = GUI.contentColor;
            GUI.contentColor = market.Watch.Direction > 0 ? new Color(.32f,.76f,.51f) : market.Watch.Direction < 0 ? new Color(.85f,.37f,.39f) : Color.white;
            GUI.Label(R(indent + 83, y + 29+extra, 71 - indent, 20), changeTips.TryGetValue(market.Identity,out GUIContent tip)?tip:new GUIContent(market.Watch.ChangeText), small);
            GUI.contentColor = oldText;
            GUI.Label(R(162, y + 25+extra, 68, 20), market.PriceShort, right);
            GUI.Label(R(162, y + 43+extra, 70, 12), "funds / unit", small);
            y += rowHeight;
        }
        private void DrawSparkline(Commodity market, Rect bounds)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (sparkTextures.TryGetValue(market.Identity, out Texture2D texture)) { Fill(bounds,texture); return; }
            Texture2D ink = market.Watch.Direction > 0 ? risingLine : market.Watch.Direction < 0 ? fallingLine : neutralLine;
            Fill(new Rect(bounds.x,bounds.center.y,bounds.width,Mathf.Max(1,scale)),ink);
        }
        private void CacheSparkline(Commodity market)
        {
            uint[] data=ChartRaster.Sparkline(market.Watch,65,12); var pixels=new Color32[data.Length];
            for(int i=0;i<data.Length;i++) pixels[i]=new Color32((byte)(data[i]>>24),(byte)(data[i]>>16),(byte)(data[i]>>8),(byte)data[i]);
            if (!sparkTextures.TryGetValue(market.Identity,out Texture2D texture))
            {
                texture=new Texture2D(65,12,TextureFormat.RGBA32,false) { hideFlags=HideFlags.HideAndDontSave };
                sparkTextures.Add(market.Identity,texture); ownedTextures.Add(texture);
            }
            texture.SetPixels32(pixels); texture.Apply();
        }
        private static string PlanetIconKey(string identity, IList<string> moons)
            => identity + "|" + (moons == null ? "" : string.Join("|", moons));
        private void CachePlanetIcon(string identity, IList<string> moons)
        {
            string key = PlanetIconKey(identity, moons);
            if (planetIcons.ContainsKey(key)) return;
            var asset=TerminalAssets.Load(TerminalAssets.BodyPath(identity,moons));
            if(asset!=null){planetIcons.Add(key,asset);return;}
            uint[] data = PlanetArt.Pixels(identity, moons); var pixels = new Color32[data.Length];
            for (int i = 0; i < data.Length; i++) pixels[i] = new Color32((byte)(data[i] >> 24), (byte)(data[i] >> 16), (byte)(data[i] >> 8), (byte)data[i]);
            int size = PlanetArt.TextureSize(moons);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels32(pixels); texture.Apply(); ownedTextures.Add(texture); planetIcons.Add(key, texture);
        }
        private void DrawPlanetIcon(string key, Rect bounds)
        {
            if (Event.current.type == EventType.Repaint && planetIcons.TryGetValue(key, out Texture2D texture)) GUI.DrawTexture(bounds, texture);
        }
        private void DrawWelcome()
        {
            if (welcomeTitle == null)
            {
                welcomeFont = Font.CreateDynamicFontFromOSFont(new[] { "Times New Roman", "Liberation Serif", "DejaVu Serif", "Georgia" }, 36);
                welcomeTitle = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Normal };
                welcomeMotto = new GUIStyle(welcomeTitle);
                welcomeDetail = new GUIStyle(welcomeTitle);
                welcomeVersion = new GUIStyle(welcomeTitle) { alignment = TextAnchor.UpperLeft };
                if (welcomeFont != null) welcomeTitle.font = welcomeMotto.font = welcomeDetail.font = welcomeVersion.font = welcomeFont;
            }
            welcomeTitle.fontSize = Mathf.Max(14, Mathf.RoundToInt(36 * scale));
            welcomeMotto.fontSize = Mathf.Max(11, Mathf.RoundToInt(19 * scale));
            Fill(R(298, 59, 725, 563), background);
            Fill(R(410, 262, 500, 1), neutralLine);
            Rect titleBounds = R(330, 278, 660, 62);
            GUI.Label(titleBounds, WelcomeTitle, welcomeTitle);
            int detailSize = Mathf.Max(9, Mathf.RoundToInt(16 * scale));
            welcomeVersion.fontSize = Mathf.Max(8, Mathf.RoundToInt(13 * scale));
            if (detailSize != welcomeDetailSize)
            {
                welcomeDetailSize = detailSize; welcomeDetail.fontSize = detailSize;
                Font font = welcomeDetail.font ?? GUI.skin.font;
                if (font != null && font.dynamic) font.RequestCharactersInTexture("\u2122", detailSize, FontStyle.Normal);
                welcomeMark = TerminalHelp.WelcomeMark(font != null && font.HasCharacter('\u2122'));
            }
            float titleRight = titleBounds.center.x + Mathf.Min(titleBounds.width, welcomeTitle.CalcSize(WelcomeTitle).x) / 2;
            welcomeDetail.alignment = TextAnchor.UpperLeft;
            GUI.Label(new Rect(titleRight + 3 * scale, titleBounds.y + 6 * scale, 32 * scale, 26 * scale), welcomeMark, welcomeDetail);
            Fill(R(410, 357, 500, 1), neutralLine);
            GUI.Label(R(410, 363, 130, 17), TerminalHelp.DisplayVersion, welcomeVersion);
            GUI.Label(R(330, 380, 660, 35), "Fortunes beyond Kerbin.", welcomeMotto);
        }
        private void DrawCommodity()
        {
            var market = session.Selection;
            if (market == null) { GUI.Label(R(310, 85, 660, 120), "No markets available. Check Prices.cfg and KSP.log.", muted); return; }
            contentScroll = GUI.BeginScrollView(R(298, 59, 725, 563), contentScroll, R(0, 0, 702, Math.Max(562,560+news.Length*74)));
            GUI.Label(R(4, 0, 440, 38), market.Title, heading);
            GUI.Label(R(4, 38, 450, 25), market.Tradeable?(market.ObjectMarket==null?"Planetary Ore":"Asteroid & Comet Ore / "+market.Secondary+""):"NOT YET TRADEABLE / "+market.Secondary+" / Market preview", muted);
            GUI.Label(R(450, 2, 244, 38), market.PriceText, price);
            DrawCountdown();
            Fill(R(4, 78, 690, 1), border);
            GUI.Label(R(4, 91, 225, 20), market.Tradeable?CurrentHelp:ObjectPriceHelp, small);
            GUI.Label(R(238, 91, 215, 20), BaseHelp, small);
            GUI.Label(R(475, 91, 215, 20), market.Tradeable?"MARKET VALUE OF 100 ORE":"REFERENCE VALUE / 100 ORE", small);
            GUI.Label(R(4, 114, 220, 26), market.PriceText, label);
            GUI.Label(R(238, 114, 220, 26), market.BasePriceText, label);
            GUI.Label(R(475, 114, 220, 26), market.Tradeable?market.HundredText:market.HundredText+" (preview)", label);
            for (int i = 0; i < Modes.Length; i++)
                if (GUI.Button(R(4 + i * 65, 158, 61, 25), ModeHelp[i], session.Mode == (ChartMode)i ? selectedTab : tab)) session.Mode = (ChartMode)i;
            for (int i = 0; i < Ranges.Length; i++)
                if (GUI.Button(R(516 + i * 44, 158, 40, 25), Ranges[i], session.Range == (MarketRange)i ? selectedTab : tab)) session.Range = (MarketRange)i;
            DrawChart();
            Fill(R(4, 442, 690, 1), border);
            GUI.Label(R(4, 454, 300, 25), NewsHelp, subheading);
            float newsY=482;
            if(news.Length==0) { GUI.Label(R(4,newsY,670,24),"No active market events.",muted);newsY+=40; }
            foreach(var item in news) {
                Color newsTint=GUI.color;if(item.Aftermath)GUI.color=new Color(newsTint.r,newsTint.g,newsTint.b,newsTint.a*.65f);
                Fill(R(4,newsY+4,3,54),item.Positive?risingLine:fallingLine);
                GUI.Label(R(14,newsY,660,23),(item.Positive?"+  ":"-  ")+"["+item.Severity+"] "+item.Headline+"  /  "+item.Scope, label);
                GUI.Label(R(14,newsY+23,660,22),item.Description,small);
                if(item.Aftermath)GUI.Label(R(14,newsY+43,130,20),AftermathHelp,small);
                GUI.Label(R(item.Aftermath?144:14,newsY+43,520,20),TerminalHelp.EventTime(item.Remaining,item.Aftermath),small);
                GUI.color=newsTint;newsY+=74;
            }
            GUI.Label(R(4,newsY,680,36),market.Reminder,small);
            GUI.EndScrollView();
        }
        private void DrawChart()
        {
            Fill(R(4, 195, 690, 233), background);
            RefreshChart();
            if (hasHistory)
            {
                Rect plot=R(20,242,558,145);
                float markerY=plot.y+(144-chartRaster.Y(session.Selection.Price,145))*scale;
                for(int i=0;i<chartRaster.Axis.Labels.Length;i++)
                {
                    double value=chartRaster.Axis.Min+i*chartRaster.Axis.Step;
                    float y=plot.y+(144-chartRaster.Y(value,145))*scale;
                    Fill(new Rect(plot.x,y,plot.width,Mathf.Max(1,scale)),border);
                    if(Math.Abs(y-markerY)>20*scale) GUI.Label(new Rect(584*scale,y-9*scale,100*scale,18*scale),chartRaster.Axis.Labels[i],axisText);
                }
                Fill(plot,chartTexture);
                Fill(new Rect(plot.x,markerY,plot.width,Mathf.Max(1,scale)),border);
                Fill(new Rect(582*scale,markerY-9*scale,103*scale,18*scale),border);
                GUI.Label(new Rect(584*scale,markerY-9*scale,98*scale,18*scale),latestLabel,axisText);
                for(int i=0;i<timeLabels.Length;i++)
                {
                    float x=Mathf.Clamp(timeXs[i]-44,0,470);
                    GUI.Label(R(20+x,392,88,17),timeLabels[i],small);
                }

                DrawHover(plot);
                DrawMarkers(plot);
                GUI.Label(R(20,221,650,18),hoverIndex==int.MinValue?"":hoverText,small);

            }
            else
            {
                Fill(R(20, 310, 657, 1), accent);
                GUI.Label(R(20, 204, 660, 24), "PRICE HISTORY", small);
                GUI.Label(R(115, 257, 510, 28), "No price history yet", subheading);
                GUI.Label(R(60, 337, 590, 38), "The first price update will appear here.", muted);
            }
        }
        private void RefreshChart()
        {
            if (chartIdentity == session.Selected && chartRevision == MarketScenario.Revision && chartMode == session.Mode && chartRange == session.Range) return;
            chartIdentity = session.Selected; chartRevision = MarketScenario.Revision; chartMode = session.Mode; chartRange = session.Range;
            monitorCaption=Modes[(int)session.Mode]+" / "+Ranges[(int)session.Range]+" active time"+(session.Selection!=null&&!session.Selection.Tradeable?" / PREVIEW":"");
            var periods = ChartHistory.Convert(MarketScenario.History(session.Selected, ChartHistory.RangeSeconds(session.Range)));
            hasHistory = periods.Length > 0;
            hoverIndex=int.MinValue;hoverText=null;markerHover=-1;chartMarkers=Array.Empty<MarketEventMarker>();markerXs=Array.Empty<double>();markerHints=Array.Empty<string>();
            if (!hasHistory) { chartCandles=Array.Empty<MarketPeriod>();chartRaster=null;return; }
            var candles = ChartHistory.ForRange(periods,session.Range);
            chartCandles=candles;
            var raster = new ChartRaster(candles, session.Mode, 558, 145,session.Selection.Price); chartRaster=raster;
            var markers=MarketScenario.Markers(session.Selected,ChartHistory.RangeSeconds(session.Range));
            var visibleMarkers=new List<MarketEventMarker>();var positions=new List<double>();var hints=new List<string>();
            foreach(var item in markers) {
                double x=EventMarkerGeometry.X(item.Time,candles,raster.Geometry,MarketScenario.IntervalSeconds);
                if(double.IsNaN(x))continue;
                visibleMarkers.Add(item);positions.Add(x);hints.Add((item.Ending?"Ended: ":"Started: ")+item.Headline+" / "+item.Scope+" / "+ChartHistory.Timestamp(item.Time)+" active time");
            }
            chartMarkers=visibleMarkers.ToArray();markerXs=positions.ToArray();markerHints=hints.ToArray();
            var pixels = new Color32[raster.Pixels.Length];
            for (int i=0;i<pixels.Length;i++) { uint c=raster.Pixels[i]; pixels[i]=new Color32((byte)(c>>24),(byte)(c>>16),(byte)(c>>8),(byte)c); }
            if (chartTexture == null) { chartTexture = new Texture2D(558,145,TextureFormat.RGBA32,false) { hideFlags=HideFlags.HideAndDontSave }; ownedTextures.Add(chartTexture); }
            chartTexture.SetPixels32(pixels); chartTexture.Apply();
            latestLabel=PriceAxis.Format(session.Selection.Price,Math.Max(2,raster.Axis.Decimals));
            int hudDecimals=Math.Max(2,raster.Axis.Decimals);
            hudPrices=new[]{PriceAxis.Format(raster.Maximum,hudDecimals),PriceAxis.Format((raster.Minimum+raster.Maximum)/2,hudDecimals),PriceAxis.Format(raster.Minimum,hudDecimals)};
            var labels=new List<string>();var xs=new List<int>();int previous=int.MaxValue;
            for(int i=candles.Length-1;i>=0;i--)
            {
                int x=raster.Geometry.X(i);
                if(previous!=int.MaxValue&&previous-x<112)continue;
                labels.Add(ChartHistory.Timestamp(candles[i].End));xs.Add(x);previous=x;
            }
            labels.Reverse();xs.Reverse();timeLabels=labels.ToArray();timeXs=xs.ToArray();
        }
        private void RefreshCountdown()
        {
            double remaining=MarketScenario.SecondsRemaining;
            int seconds=double.IsNaN(remaining)?-1:(int)Math.Ceiling(remaining);
            if(seconds!=countdownSecond||monitorOrigin!=session.Selected||monitorEventRevision!=MarketScenario.Revision) {
                monitorEventRevision=MarketScenario.Revision;
                countdownSecond=seconds;monitorOrigin=session.Selected;news=MarketScenario.News();
                countdownText="NEXT MARKET UPDATE  "+ChartHistory.Countdown(remaining);updateHelp.text=countdownText;
                monitorCountdown="Next "+ChartHistory.Countdown(remaining);
                var relevant=MarketScenario.NewsFor(session.Selected);
                monitorEvents=MonitorEvent.Select(session.Selected,relevant);
                if(monitor.Compact) {
                    int active=0;foreach(var item in relevant)if(!item.Aftermath)active++;
                    string labels="";foreach(var item in monitorEvents)labels+=(labels.Length==0?"":", ")+item.Text;
                    string signature=session.Selected+"|"+MarketScenario.Ready+"|"+active+"|"+(relevant.Length-active)+"|"+labels;
                    if(signature!=monitorDiagnostic) {
                        monitorDiagnostic=signature;
                        Debug.Log("[OreMarket.UI] Monitor events: selected="+session.Selected+" ready="+MarketScenario.Ready+" active="+active+" fading="+(relevant.Length-active)+" labels="+(monitorEvents.Length==0?"(none)":labels));
                    }
                }
                monitorEventHints=new GUIContent[monitorEvents.Length];
                for(int i=0;i<monitorEvents.Length;i++)monitorEventHints[i]=new GUIContent(monitorEvents[i].Text,monitorEvents[i].Hint);
                monitorPrice.text=session.Selection==null?"":session.Selection.PriceText;
                monitorPrice.tooltip=session.Selection!=null&&!session.Selection.Tradeable?"No verified sales are available for this commodity.":TerminalHelp.CurrentPrice;
            }
        }
        private void DrawCountdown()
        {
            RefreshCountdown();GUI.Label(R(450,41,244,22),updateHelp,axisText);
        }
        private void DrawHover(Rect plot)
        {
            Vector2 mouse=Event.current.mousePosition;
            if(!plot.Contains(mouse)) { hoverIndex=int.MinValue;return; }
            int index=chartRaster.Geometry.Nearest((mouse.x-plot.x)/scale);
            if(index!=hoverIndex)
            {
                hoverIndex=index;var p=chartCandles[Math.Max(0,index)];
                string time=ChartHistory.Timestamp(index<0?Math.Max(0,p.Start-MarketScenario.IntervalSeconds):p.End);
                int decimals=Math.Max(2,chartRaster.Axis.Decimals);
                if((session.Mode==ChartMode.Candle||session.Mode==ChartMode.Bar)&&index>=0)
                    hoverText=time+"  O "+PriceAxis.Format(p.Open,decimals)+"  H "+PriceAxis.Format(p.High,decimals)+"  L "+PriceAxis.Format(p.Low,decimals)+"  C "+PriceAxis.Format(p.Close,decimals);
                else hoverText=time+"  Price "+PriceAxis.Format(index<0?p.Open:p.Close,decimals)+" funds / unit";
            }
            int x=hoverIndex<0?0:chartRaster.Geometry.X(hoverIndex);
            var point=chartCandles[Math.Max(0,hoverIndex)];double value=hoverIndex<0?point.Open:point.Close;
            float y=plot.y+(144-chartRaster.Y(value,145))*scale;
            Fill(new Rect(plot.x+x*scale,plot.y,Mathf.Max(1,scale),plot.height),neutralLine);
            Fill(new Rect(plot.x,y,plot.width,Mathf.Max(1,scale)),border);
        }
        private void SetCompact(bool compact)
        {
            if(monitor.Compact==compact)return;
            if(monitor.Compact)compactWindow=window;else fullWindow=window;
            monitor.SetCompact(compact);window=compact?compactWindow:fullWindow;
            helpVisible=false;ReleaseLock();FitWindow();
        }
        private void HudLabel(Rect area,string text,GUIStyle style,Color ink)
        {
            Color previous=GUI.contentColor;var shadow=area;shadow.x+=scale;shadow.y+=scale;
            GUI.contentColor=new Color(0,0,0,.9f);GUI.Label(shadow,text,style);
            GUI.contentColor=ink;GUI.Label(area,text,style);GUI.contentColor=previous;
        }
        private void HudContent(Rect area,GUIContent content,GUIStyle style,Color ink)
        {
            Color previous=GUI.contentColor;var shadow=area;shadow.x+=scale;shadow.y+=scale;
            GUI.contentColor=new Color(0,0,0,.9f);GUI.Label(shadow,content.text,style);
            GUI.contentColor=ink;GUI.Label(area,content,style);GUI.contentColor=previous;
        }
        private void HudRule(Rect area,Texture2D ink)
        {
            var shadow=area;shadow.x+=scale;shadow.y+=scale;Fill(shadow,hudRuleShadow);Fill(area,ink);
        }
        private void DrawCompact()
        {
            Color white=Tone(MonitorHud.Text),secondary=Tone(MonitorHud.Secondary);
            Fill(R(0,0,MonitorLayout.Width,MonitorLayout.Height),monitorBackground);
            bool maximize=GUI.Button(R(280,7,50,24),"",hudButton);
            HudLabel(R(280,7,50,24),"MAX",hudButtonText,white);
            if(maximize){requestedCompact=false;return;}
            bool close=GUI.Button(R(342,7,25,24),"",hudButton);
            HudLabel(R(342,7,25,24),"X",hudButtonText,white);
            if(close){Close();return;}
            HudRule(R(280,31,50,1),hudLine);HudRule(R(342,31,25,1),hudLine);
            GUI.DragWindow(R(0,0,274,33));
            var market=session.Selection;
            HudLabel(R(12,7,258,24),market==null?"The Kerbol Exchange":market.Title,hudTitle,white);
            if(session.Welcome || market==null){HudLabel(R(12,55,350,80),"Use MAX to select a market.",hudTooltip,white);return;}
            RefreshCountdown();RefreshChart();
            HudContent(R(12,40,238,26),monitorPrice,hudText,white);
            Color movement=market.Watch.Direction>0?Tone(MonitorHud.Rise):market.Watch.Direction<0?Tone(MonitorHud.Fall):white;
            if(changeTips.TryGetValue(market.Identity,out GUIContent change))HudContent(R(252,40,115,26),change,hudRight,movement);
            HudLabel(R(12,65,220,17),"CHANGE: UP TO 1H ACTIVE",hudSmall,secondary);
            HudLabel(R(252,65,115,17),monitorCountdown,hudAxis,white);
            Rect plot=R(12,87,300,115);
            if(hasHistory) {
                for(int i=0;i<3;i++) {
                    float y=87+i*57.5f;
                    HudRule(R(12,y,300,1),hudGrid);
                    HudLabel(R(316,y-7,54,17),hudPrices[i],hudAxis,white);
                }
                Color color=GUI.color;GUI.color=new Color(0,0,0,.85f);
                Fill(new Rect(plot.x+scale,plot.y+scale,plot.width,plot.height),chartTexture);
                GUI.color=color;Fill(plot,chartTexture);
            } else HudLabel(R(20,110,280,65),"Waiting for recorded market history.",hudTooltip,white);
            HudRule(R(313,87,1,115),hudLine);HudRule(R(12,202,302,1),hudLine);
            HudLabel(R(12,204,350,18),monitorCaption,hudSmall,secondary);
            Fill(R(10,225,360,27),hudEventBackground);
            HudLabel(R(14,227,52,22),"EVENTS",hudSmall,white);
            if(monitorEvents.Length==0)HudLabel(R(70,227,292,22),"No active events",hudSmall,secondary);
            for(int i=0;i<monitorEvents.Length;i++) {
                float width=monitorEvents.Length==1?292:145;
                HudContent(R(70+i*148,226,width,24),monitorEventHints[i],hudText,monitorEvents[i].Positive?Tone(MonitorHud.Rise):Tone(MonitorHud.Fall));
            }
            if(!string.IsNullOrEmpty(GUI.tooltip)&&new Rect(0,0,window.width,window.height).Contains(Event.current.mousePosition)) {
                Fill(R(12,87,356,116),hudTooltipBackground);
                HudLabel(R(20,92,340,106),GUI.tooltip,hudTooltip,white);
            }
        }
        private void DrawMarkers(Rect plot)
        {
            for(int i=0;i<chartMarkers.Length;i++) {
                float x=plot.x+(float)markerXs[i]*scale;
                var item=chartMarkers[i];
                Fill(new Rect(x,plot.y,Mathf.Max(1,scale),plot.height),item.Ending?neutralLine:item.Positive?risingLine:fallingLine);
            }
            if(helpVisible||!plot.Contains(Event.current.mousePosition)){markerHover=-1;return;}
            double mouse=(Event.current.mousePosition.x-plot.x)/scale;
            int nearest=EventMarkerGeometry.Nearest(mouse,markerXs,4);
            if(nearest<0){markerHover=-1;return;}
            if(markerHover!=nearest) {
                markerHover=nearest;var text=new System.Text.StringBuilder();int count=0;
                for(int i=0;i<markerXs.Length;i++)if(Math.Abs(markerXs[i]-markerXs[nearest])<=4) {
                    if(count<3){if(count>0)text.Append("\n");text.Append(markerHints[i]);}count++;
                }
                if(count>3)text.Append("\n+").Append(count-3).Append(" nearby markers; use a shorter range to separate them.");
                cachedMarkerHint=text.ToString();
            }
            markerHint=cachedMarkerHint;
        }
        private string cachedMarkerHint;
        private void DrawHint()
        {
            if(!new Rect(0,0,window.width,window.height).Contains(Event.current.mousePosition))return;
            string hint=markerHint??GUI.tooltip;if(string.IsNullOrEmpty(hint))return;
            Fill(R(294,516,723,108),border);Fill(R(295,517,721,106),surface);
            GUI.Label(R(305,523,700,94),hint,muted);
        }
        private void DrawHelp()
        {
            Fill(R(286,91,692,515),border);Fill(R(287,92,690,513),background);
            GUI.Label(R(309,108,580,30),"THE KERBOL EXCHANGE / QUICK GUIDE",subheading);
            GUI.Label(R(309,151,640,370),TerminalHelp.Guide,muted);
            if(GUI.Button(R(829,554,120,30),"Done",tab))helpVisible=false;
        }
        private Texture2D Tile(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color); texture.Apply(); ownedTextures.Add(texture); return texture;
        }
        private GUIStyle Text(int size, Color color, bool bold = false)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, richText = false,
                wordWrap = true, padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleLeft };
            style.normal.textColor = color; return style;
        }
        private GUIStyle Button(Texture2D normal, Texture2D hover, Color text)
        {
            var style = new GUIStyle(GUI.skin.button) { fontSize = 12, richText = false, border = new RectOffset(), margin = new RectOffset(), alignment = TextAnchor.MiddleLeft };
            style.padding = new RectOffset(10, 4, 0, 0);
            style.normal.background = normal; style.hover.background = hover; style.active.background = hover; style.focused.background = hover;
            style.onNormal.background = style.onHover.background = style.onActive.background = style.onFocused.background = hover;
            style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = text;
            style.normal.textColor = text; style.hover.textColor = text; style.active.textColor = text; style.focused.textColor = text;
            return style;
        }
        private static Color Tone(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        private static void ScrollStyle(GUIStyle style, Texture2D normal, Texture2D active)
        {
            style.normal.background = style.onNormal.background = normal;
            style.hover.background = style.onHover.background = style.active.background = style.onActive.background =
                style.focused.background = style.onFocused.background = active;
        }
        private void BuildStyles()
        {
            background = Tile(Tone(TerminalPalette.Background)); surface = Tile(Tone(TerminalPalette.Panel));
            neutralLine = Tile(Tone(TerminalPalette.Baseline)); risingLine = Tile(new Color(0.32f, 0.76f, 0.51f)); fallingLine = Tile(new Color(0.85f, 0.37f, 0.39f));
            border = Tile(Tone(TerminalPalette.Border)); accent = Tile(Tone(TerminalPalette.Baseline));
            var hover = Tile(Tone(TerminalPalette.Hover)); var selection = Tile(Tone(TerminalPalette.Selected));
            Color white = Tone(TerminalPalette.Primary), gray = Tone(TerminalPalette.Secondary);
            panel = new GUIStyle(GUI.skin.window) { border = new RectOffset(), padding = new RectOffset() }; panel.normal.background = panel.onNormal.background = panel.hover.background = panel.onHover.background =
                panel.active.background = panel.onActive.background = panel.focused.background = panel.onFocused.background = surface;
            // CTB uses the nonempty caption as its lock identity; hide only this
            // native title because the window draws its own visible header.
            panel.normal.textColor = panel.onNormal.textColor = panel.hover.textColor = panel.onHover.textColor =
                panel.active.textColor = panel.onActive.textColor = panel.focused.textColor = panel.onFocused.textColor = Color.clear;
            monitorBackground=Tile(new Color(1,1,1,MonitorHud.BackgroundAlpha));
            var clear=Tile(Color.clear);monitorPanel=new GUIStyle(panel);
            monitorPanel.normal.background=monitorPanel.onNormal.background=monitorPanel.hover.background=monitorPanel.onHover.background=
                monitorPanel.active.background=monitorPanel.onActive.background=monitorPanel.focused.background=monitorPanel.onFocused.background=clear;
            axisText=Text(10,white);axisText.alignment=TextAnchor.MiddleRight;axisText.wordWrap=false;
            heading = Text(21, white, true); subheading = Text(13, white, true); label = Text(13, white);
            muted = Text(12, gray); small = Text(10, gray); price = Text(17, white, true);
            price.alignment = TextAnchor.UpperRight; right = Text(13, white); right.alignment = TextAnchor.MiddleRight;
            row = Button(surface, hover, white); selectedRow = Button(selection, hover, white);
            tab = Button(surface, hover, gray); selectedTab = Button(selection, hover, white);
            tab.alignment = selectedTab.alignment = TextAnchor.MiddleCenter;
            tab.padding = selectedTab.padding = new RectOffset();
            groupHeader = new GUIStyle(tab) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 0, 0, 0) };
            skin = Instantiate(GUI.skin);
            // Override every scrollbar state so inherited KSP hover/pressed
            // art cannot reintroduce coloured accents. Geometry is unchanged.
            ScrollStyle(skin.verticalScrollbar, background, background);
            ScrollStyle(skin.horizontalScrollbar, background, background);
            ScrollStyle(skin.verticalScrollbarThumb, border, selection);
            ScrollStyle(skin.horizontalScrollbarThumb, border, selection);
            ScrollStyle(skin.verticalScrollbarUpButton, surface, hover);
            ScrollStyle(skin.verticalScrollbarDownButton, surface, hover);
            ScrollStyle(skin.horizontalScrollbarLeftButton, surface, hover);
            ScrollStyle(skin.horizontalScrollbarRightButton, surface, hover);
            BuildHudStyles();UpdateFonts();
        }
        private void BuildHudStyles()
        {
            hudTitle=Text(13,Color.white,true);hudTitle.wordWrap=false;
            hudText=Text(13,Color.white);hudText.wordWrap=false;
            hudSmall=Text(10,Color.white);hudSmall.wordWrap=false;
            hudRight=new GUIStyle(hudText){alignment=TextAnchor.MiddleRight};
            hudAxis=new GUIStyle(hudSmall){alignment=TextAnchor.MiddleRight};
            hudTooltip=Text(12,Color.white);
            hudButtonText=new GUIStyle(hudText){alignment=TextAnchor.MiddleCenter};
            hudRuleShadow=Tile(new Color(0,0,0,.65f));
            hudLine=Tile(new Color(1,1,1,.55f));hudGrid=Tile(new Color(1,1,1,.22f));
            hudTooltipBackground=Tile(new Color(0,0,0,.78f));hudEventBackground=Tile(new Color(0,0,0,.14f));
            hudButton=Button(Tile(Color.clear),Tile(new Color(1,1,1,.12f)),Color.white);
            hudButton.alignment=TextAnchor.MiddleCenter;hudButton.padding=new RectOffset();
        }
        private void UpdateFonts()
        {
            hudTitle.fontSize=hudText.fontSize=hudRight.fontSize=Mathf.Max(11,Mathf.RoundToInt(13*scale));
            hudSmall.fontSize=hudAxis.fontSize=Mathf.Max(9,Mathf.RoundToInt(10*scale));
            hudTooltip.fontSize=hudButton.fontSize=hudButtonText.fontSize=Mathf.Max(11,Mathf.RoundToInt(12*scale));
            heading.fontSize = Mathf.Max(13, Mathf.RoundToInt(21 * scale)); subheading.fontSize = Mathf.Max(11, Mathf.RoundToInt(13 * scale));
            label.wordWrap = false;
            label.fontSize = right.fontSize = Mathf.Max(11, Mathf.RoundToInt(13 * scale));
            axisText.fontSize=Mathf.Max(9,Mathf.RoundToInt(10*scale));
            small.fontSize = Mathf.Max(9, Mathf.RoundToInt(10 * scale)); muted.fontSize = Mathf.Max(11, Mathf.RoundToInt(12 * scale));
            price.fontSize = Mathf.Max(12, Mathf.RoundToInt(17 * scale));
            row.fontSize = selectedRow.fontSize = tab.fontSize = selectedTab.fontSize = groupHeader.fontSize = Mathf.Max(11, Mathf.RoundToInt(12 * scale));
        }
        private Texture2D MakeIcon()
        {
            var texture = new Texture2D(38, 38, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[38 * 38];
            for (int y = 0; y < 38; y++) for (int x = 0; x < 38; x++)
            {
                bool bars = (x >= 5 && x < 12 && y >= 5 && y < 14) || (x >= 16 && x < 23 && y >= 5 && y < 22) || (x >= 27 && x < 34 && y >= 5 && y < 32);
                bool ore = Math.Abs(x - 11) + Math.Abs(y - 28) <= 6;
                if (bars || ore) pixels[y * 38 + x] = new Color32(232, 232, 232, 255);
            }
            texture.SetPixels32(pixels); texture.Apply(); return texture;
        }
        public void OnDestroy()
        {
            if (instance != this) return;
            RemoveButton(); ReleaseLock();
            if (subscribed)
            {
                GameEvents.onGUIApplicationLauncherReady.Remove(EnsureButton);
                GameEvents.onGUIApplicationLauncherDestroyed.Remove(RemoveButton);
                GameEvents.onGameSceneLoadRequested.Remove(SceneChanging);
                GameEvents.onHideUI.Remove(HideUI);
                GameEvents.onShowUI.Remove(ShowUI);
                subscribed = false;
            }
            if (welcomeFont != null) Destroy(welcomeFont);
            if (skin != null) Destroy(skin);
            foreach (var texture in ownedTextures) if (texture != null) Destroy(texture);
            ownedTextures.Clear(); planetIcons.Clear(); sparkTextures.Clear(); if (iconOwned && icon != null) Destroy(icon);
            instance = null;
        }
    }
}






