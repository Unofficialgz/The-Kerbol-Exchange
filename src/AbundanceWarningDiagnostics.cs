using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace OreMarket
{
    // Diagnostic observer ONLY. It reads already-posted messages and fields;
    // it cannot identify the originating caller from a later frame snapshot.
    // No Harmony, recipe probes, resource queries, warning removal or drill control.
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class AbundanceWarningDiagnostics : MonoBehaviour
    {
        private readonly Dictionary<ScreenMessage, float> seen = new Dictionary<ScreenMessage, float>();
        private float nextLog;
        private int refreshes;
        private bool failed;

        public void Start()
        {
            Log.Info("0.2.4 passive abundance-warning diagnostics active; mining rules unchanged");
        }
        internal static bool Matches(string text) => text != null &&
            (text.IndexOf("Insufficient resource abundance", StringComparison.OrdinalIgnoreCase) >= 0 ||
             text.IndexOf("#autoLOC_6001069", StringComparison.Ordinal) >= 0);

        public void LateUpdate()
        {
            if (failed || ScreenMessages.Instance == null || ScreenMessages.Instance.ActiveMessages == null) return;
            try
            {
                if (ScreenMessages.Instance.ActiveMessages.Count == 0) { seen.Clear(); refreshes = 0; return; }
                ScreenMessage[] warnings = ScreenMessages.Instance.ActiveMessages.Where(m => m != null && Matches(m.message)).ToArray();
                var active = new HashSet<ScreenMessage>(warnings);
                foreach (ScreenMessage old in seen.Keys.Where(m => !active.Contains(m)).ToArray()) seen.Remove(old);
                foreach (ScreenMessage warning in warnings)
                {
                    if (seen.TryGetValue(warning, out float last) && last == warning.startTime) continue;
                    seen[warning] = warning.startTime;
                    refreshes++;
                    // Logging throttle only. It never participates in attribution.
                    if (Time.realtimeSinceStartup < nextLog) continue;
                    Log.Info("Abundance warning observed (passive snapshot, NOT caller trace): text=" + warning.message +
                        " refreshesSinceLog=" + refreshes + " " + Describe(FlightGlobals.ActiveVessel));
                    refreshes = 0;
                    nextLog = Time.realtimeSinceStartup + 5f;
                }
            }
            catch (Exception e)
            {
                failed = true; // Stop only this optional logger, never production/tracking.
                Log.Error("Passive abundance-warning diagnostics unavailable: " + e.GetType().Name);
            }
        }

        internal static string Describe(Vessel vessel)
        {
            if (vessel == null) return "activeVessel=none";
            var entries = new List<string>();
            foreach (Part part in vessel.parts)
            {
                if (part == null) continue;
                foreach (ModuleResourceHarvester drill in part.Modules.Cast<PartModule>().OfType<ModuleResourceHarvester>())
                {
                    string deployment = string.Join(",", part.Modules.Cast<PartModule>().OfType<ModuleAnimationGroup>()
                        .Select(g => g.isDeployed ? "deployed" : "retracted"));
                    entries.Add("part=" + part.partInfo?.name + " id=" + part.persistentId +
                        " resource=" + drill.ResourceName + " active=" + drill.IsActivated +
                        " deployment=" + (deployment.Length == 0 ? "no-animation-group" : deployment) +
                        " threshold=" + drill.HarvestThreshold.ToString("R", CultureInfo.InvariantCulture) +
                        " stockStatus=" + drill.status);
                }
            }
            return "vessel=" + vessel.id + " body=" + vessel.mainBody?.bodyName +
                " loaded=" + vessel.loaded + " packed=" + vessel.packed + " drills=[" + string.Join("; ", entries) + "]";
        }
    }
}

