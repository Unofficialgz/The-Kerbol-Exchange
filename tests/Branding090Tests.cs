using System;
using System.Linq;
using System.Collections.Generic;
using OreMarket.Terminal;
internal static class Branding090Tests {
 internal static void Run(Action<string,bool> check) {
  var bodies=new[]{new MarketBody{Identity="Sun",Star=true},new MarketBody{Identity="Kerbin",Parent="Sun"},new MarketBody{Identity="Mun",Parent="Kerbin",Price=40},new MarketBody{Identity="Duna",Parent="Sun",Price=75},new MarketBody{Identity="Ike",Parent="Duna",Price=65},new MarketBody{Identity="Dres",Parent="Sun",Price=105}};
  var groups=MarketCatalog.Build(bodies);var s=new TerminalSession{Selected="Duna",Mode=ChartMode.Bar,Range=MarketRange.Hours6};s.Bind(groups);
  check("090: every expandable group initially collapsed",groups.Where(g=>g.Expandable).All(g=>s.Collapsed.Contains(g.Identity))&&s.Collapsed.Count==2);
  check("090: direct markets remain direct",groups.Where(g=>!g.Expandable).All(g=>!s.Collapsed.Contains(g.Identity)));
  check("090: hidden selected market and chart preserved",s.Selected=="Duna"&&s.Selection.Title=="Duna Ore"&&s.Mode==ChartMode.Bar&&s.Range==MarketRange.Hours6);
  s.Collapsed.Remove("Duna");s.Select(groups.Single(g=>g.Identity=="Duna").Markets.Single(m=>m.Identity=="Ike"));
  for(int i=0;i<20;i++)s.Bind(MarketCatalog.Build(bodies));
  check("090: expansion and selection survive refreshes/scene rebind",!s.Collapsed.Contains("Duna")&&s.Collapsed.Contains("Kerbin")&&s.Selected=="Ike");
  s.Collapsed.Add("Duna");s.Bind(groups);check("090: user collapse stays collapsed",s.Collapsed.Contains("Duna")&&s.Selected=="Ike");
  var fresh=new TerminalSession{Selected=s.Selected};fresh.Bind(groups);
  check("090: new KSP session starts collapsed again without changing selection",fresh.Collapsed.SetEquals(new[]{"Kerbin","Duna"})&&fresh.Selected=="Ike");
  var extra=new MarketGroup{Identity="PackWorld",Expandable=true};extra.Markets.Add(new Commodity("PackMoon",10));groups.Add(extra);s.Collapsed.Remove("Duna");s.Bind(groups);
  check("090: newly discovered planet-pack group collapses once only",s.Collapsed.Contains("PackWorld")&&!s.Collapsed.Contains("Duna"));
  s.Collapsed.Remove("PackWorld");s.Bind(groups);check("090: planet-pack user expansion retained",!s.Collapsed.Contains("PackWorld"));
  check("090: concise help retains gameplay restrictions",TerminalHelp.Guide.Contains("preloaded")&&TerminalHelp.Guide.Contains("career mode")&&TerminalHelp.Guide.Contains("not locked")&&TerminalHelp.Update.Contains("time warp"));
  check("090: market reminder has no prototype/development prose",!s.Selection.Reminder.Contains("prototype")&&s.Selection.Reminder.Contains("Kerbin")&&s.Selection.Reminder.Contains("current price"));
  var owner=new object();s.BeginSave(owner);
  check("093: loading a save shows welcome without changing chart settings",s.Welcome&&s.Mode==ChartMode.Bar&&s.Range==MarketRange.Hours6);
  s.Select(groups.Single(g=>g.Identity=="Duna").Markets.First());s.BeginSave(owner);s.Bind(groups);
  check("093: selection dismisses welcome across refreshes and window reopening",!s.Welcome&&s.Selected=="Duna");
  s.BeginSave(new object());
  check("093: another save session restores welcome",s.Welcome&&s.Selected=="Duna");
  s.Select(groups.Single(g=>g.Identity=="Duna").Markets.First());s.LeaveSave();s.BeginSave(owner);
  check("093: main menu resets welcome even when a game object is reused",s.Welcome);
  check("094: welcome version reads public file version rather than retained assembly version",TerminalHelp.VersionLabel("0.9.4.0")=="V0.9.4");
  check("094: welcome version follows future metadata",TerminalHelp.VersionLabel("1.2.3.4")=="V1.2.3");
  check("094: unavailable version is not falsely reported",TerminalHelp.VersionLabel(null)=="V?"&&TerminalHelp.VersionLabel("bad")=="V?"&&TerminalHelp.VersionLabel("0.9")=="V?");
  check("094: trademark uses the real glyph when available and ASCII fallback otherwise",TerminalHelp.WelcomeMark(true)=="\u2122"&&TerminalHelp.WelcomeMark(false)=="TM");
  check("100: null tracking input fails closed",OreMarket.TrackingState.Load(null).Invalid);
  var broken=OreMarket.TrackingState.New(0,100).Save();broken["origin"]=null;
  check("100: null tracking field fails closed without credit",OreMarket.TrackingState.Load(broken).Invalid&&OreMarket.TrackingState.Load(broken).Eligible==0);
  check("100: public version formatting",TerminalHelp.VersionLabel("1.0.0.0")=="V1.0.0");
 }
}
