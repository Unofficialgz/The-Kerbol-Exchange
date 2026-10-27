using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace OreMarket.Terminal {
 internal static class TerminalAssets {
  internal const string Root="KerbolExchange/Textures/";
  private static readonly Dictionary<string,Texture2D> cache=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
  private static readonly HashSet<string> bodies=new HashSet<string>(new[]{"Kerbin","Mun","Minmus","Eve","Gilly","Duna","Ike","Jool","Laythe","Vall","Tylo","Bop","Pol","Moho","Dres","Eeloo"},StringComparer.Ordinal);
  internal static string BodyPath(string identity,IList<string> moons) {
   if(ObjectMarkets.Reserved(identity))return Root+"Comet and Asteroid Icon";
   if(!bodies.Contains(identity))return null;
   if(moons==null||moons.Count==0)return Root+"Bodies/"+identity;
   string[] expected=identity=="Kerbin"?new[]{"Mun","Minmus"}:identity=="Eve"?new[]{"Gilly"}:identity=="Duna"?new[]{"Ike"}:identity=="Jool"?new[]{"Laythe","Vall","Tylo","Bop","Pol"}:Array.Empty<string>();
   // Planet packs can change moon membership/order. Generate their actual diagram.
   return moons.SequenceEqual(expected)?Root+"Systems/"+identity:null;
  }
  internal static Texture2D Load(string path) {
   if(path==null)return null;
   if(cache.TryGetValue(path,out var texture))return texture;
   try{texture=GameDatabase.Instance?.GetTexture(path,false);}catch(Exception e){Debug.LogWarning("[OreMarket.UI] Texture unavailable: "+path+" "+e.Message);}
   cache[path]=texture;
   if(texture==null)Debug.LogWarning("[OreMarket.UI] Missing texture: "+path+"; using cached fallback");
   return texture;
  }
 }
}
