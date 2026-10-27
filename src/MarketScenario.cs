using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace OreMarket
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.EDITOR)]
    public sealed class MarketScenario : ScenarioModule
    {
        private static MarketScenario instance;
        private static long generation;
        private Game owner;
        private MarketBook book;
        private ActiveMarketClock clock = new ActiveMarketClock();
        private ConfigNode[] rejected = Array.Empty<ConfigNode>();
        private bool loaded, changing, gamePaused;
        public static bool Ready => HighLogic.CurrentGame != null && instance != null && instance.loaded && ReferenceEquals(instance.owner, HighLogic.CurrentGame);
        public static long Revision => generation * 1000000000L + (Ready ? instance.book.Revision : 0);
        public static double IntervalSeconds => Ready ? instance.book.IntervalSeconds : 60;
        public static double SecondsRemaining => Ready ? instance.book.SecondsRemaining : double.NaN;
        public static double ActiveSeconds => Ready ? instance.book.Elapsed : 0;
        internal static MarketBook ActiveBook => Ready ? instance.book : null;
        public static MarketEventMarker[] Markers(string origin,double seconds) => Ready ? instance.book.Markers(ObjectMarkets.Commodity(origin),seconds) : Array.Empty<MarketEventMarker>();
        public static MarketSample[] History(string origin, double seconds) => Ready ? instance.book.History(ObjectMarkets.Commodity(origin), seconds) : Array.Empty<MarketSample>();
        internal static bool Price(string identity, double baseline, out double value)
        {
            value = 0;
            // Outside a loaded market scenario this is a base-price preview, not
            // a second evolving market. A present but invalid scenario fails closed.
            if (instance == null) { value = baseline; return true; }
            if (!Ready) return false;
            value = instance.book.Ensure(identity, baseline); return value > 0;
        }
        public override void OnAwake()
        {
            base.OnAwake(); instance = this;
            GameEvents.onGameSceneLoadRequested.Add(SceneChanging);
            GameEvents.onGamePause.Add(GamePaused); GameEvents.onGameUnpause.Add(GameUnpaused);
        }
        private void GamePaused() { gamePaused=true; clock.Sample(Now,false); Log.Info("Market clock paused; no catch-up time will accrue"); }
        private void GameUnpaused() { gamePaused=false; clock.Sample(Now,false); Log.Info("Market clock resumed; inactive interval discarded"); }
        public static MarketNews[] NewsFor(string origin) => Ready ? instance.book.NewsFor(ObjectMarkets.Commodity(origin)) : Array.Empty<MarketNews>();
        public static MarketNews[] News() => Ready ? instance.book.News() : Array.Empty<MarketNews>();
        private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        private static bool Supported => HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.FLIGHT ||
            HighLogic.LoadedScene == GameScenes.TRACKSTATION || HighLogic.LoadedScene == GameScenes.EDITOR;
        private void SceneChanging(GameScenes scene) { changing = true; clock.Sample(Now, false); }
        public void OnApplicationFocus(bool focused) { clock.Sample(Now, false); }
        public void OnApplicationPause(bool paused) { clock.Sample(Now, false); }
        public void Update() => Sample(Now);
        internal void Sample(double now)
        {
            bool active = Ready && instance == this && !changing && !gamePaused && !(HighLogic.LoadedScene == GameScenes.SPACECENTER && Planetarium.Pause) && Supported && UnityEngine.Application.isFocused &&
                !(PauseMenu.exists && PauseMenu.isOpen) && !FlightDriver.Pause;
            double delta = clock.Sample(now, active);
            if (active && delta > 0)
            {
                long previous = book.Updates; book.Advance(delta);
                if (book.Updates != previous) Log.Info("Market interval: update=" + book.Updates + " activeSeconds=" + book.Elapsed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node); loaded = false; book = null; changing = false; owner = HighLogic.CurrentGame;
            instance = this; generation++; clock = new ActiveMarketClock(); rejected = node.GetNodes("MARKET_STATE");
            try
            {
                if (rejected.Length > 1) throw new FormatException("Duplicate MARKET_STATE nodes");
                book = rejected.Length == 0 ? new MarketBook(OrePricing.Settings, MarketBook.SaveSeed(HighLogic.SaveFolder, owner == null ? 0 : owner.Seed)) :
                    MarketBook.Load(rejected[0], OrePricing.Settings);
                if(MarketQuotes.TryGetBasePrice(ObjectMarkets.Shared,out double sharedBase))book.MigrateSpaceRocks(sharedBase);
                var eligible=new List<string>();
                if (FlightGlobals.Bodies != null) foreach (var body in FlightGlobals.Bodies)
                    if (body != null && OrePricing.TryGetBasePrice(body.bodyName, out double baseline)) { book.Ensure(body.bodyName, baseline);eligible.Add(body.bodyName);
                        var parent=body.referenceBody; book.SetSystem(body.bodyName,parent!=null && parent.referenceBody!=null && parent.referenceBody!=parent ? parent.bodyName : body.bodyName); }
                foreach(var d in ObjectMarkets.Definitions)if(MarketQuotes.TryGetBasePrice(d.Identity,out double basePrice)){book.Ensure(d.Identity,basePrice);eligible.Add(d.Identity);}
                if(FlightGlobals.Bodies!=null) book.SetEligibleOrigins(eligible);
                loaded = true; rejected = Array.Empty<ConfigNode>();
                Log.Info("market state " + (node.GetNodes("MARKET_STATE").Length == 0 ? "initialized" : "loaded") +
                    ": update=" + book.Updates + " timer=" + book.Progress + "; active real gameplay time only");
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is OverflowException)
            { book = null; Log.Error("Market state invalid; live quotes unavailable, saved data retained: " + e.Message); }
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node); node.RemoveNodes("MARKET_STATE");
            if (loaded && book != null) book.Save(node.AddNode("MARKET_STATE"));
            else foreach (var raw in rejected) node.AddNode(raw);
        }
        public void OnDestroy()
        {
            GameEvents.onGameSceneLoadRequested.Remove(SceneChanging);
            GameEvents.onGamePause.Remove(GamePaused); GameEvents.onGameUnpause.Remove(GameUnpaused);
            if (instance == this) { instance = null; generation++; }
        }
    }
}
