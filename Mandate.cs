using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CSharp;

using Sabertooth.Lexicon;
using Sabertooth.Lexicon.Attributes;

namespace Sabertooth.Mandate {
	internal enum SiteMethod {GET, POST, REQAUTH, ISAUTH}
	public class MandateManager {
		protected HashSet<Mandate> mandates;
		protected ConcurrentDictionary<string, Mandate> subdomainDict;
		protected Mandate rootMandate;
		public MandateManager() { 
			this.RebuildMandates ();
		}
		protected Thread Watchdog;
		protected ManualResetEvent Watchgate = new ManualResetEvent(false);
		protected bool watching = false;
		protected bool validationflag = false;
		protected void WatchdogMethod () { 
			while (watching) {
				this.Watchgate.WaitOne ();
				do {
					this.validationflag = false;
					this.RebuildReferences();
				} while (this.validationflag);
				this.Watchgate.Reset ();
			}
		}
		public void Begin() {
			if (Watchdog == null) {
				this.watching = true;
				Watchgate.Reset ();
				Watchdog = new Thread (WatchdogMethod);
				Watchdog.Start ();
			}
		}
		public void End() {
			if (Watchdog != null) {
				this.watching = false;
				Watchgate.Set ();
				Watchdog.Join ();
				Watchdog = null;
			}
		}
		public void RebuildMandates() {
			if (mandates != null) {
				foreach(Mandate M in mandates) {
					M.End ();
				}
			}
			mandates = new HashSet<Mandate> ();
			foreach(string path in Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, "Sites"), "*.sbr", SearchOption.TopDirectoryOnly)) {
				mandates.Add(new Mandate (path));
			}
			foreach(Mandate M in mandates) {
				if(!M.Build ()) {
					Console.WriteLine ("Mandate \"{0}\" is not valid, and because it was invalid on launch, it has no fallback iterations in memory.", M.Filename);
				}
				M.MandateBuildSuccess += OnMandateRebuilt;
				M.MandateBuildFailure += OnMandateFailedRebuild;
				M.Begin ();
			}
			this.RebuildReferences ();
		}
		public void RebuildReferences() {
			subdomainDict = new ConcurrentDictionary<string, Mandate> ();
			rootMandate = null;
			foreach (Mandate M in mandates) {
				if (M.ClaimsRoot) {
					if (rootMandate != null) {
						Console.WriteLine ("WARNING: MULTIPLE CLAIMS TO ROOT DETECTED!");
					}
					rootMandate = M;
				}
				foreach (string sub in M.Subdomains) {
					if (subdomainDict.ContainsKey (sub)) {
						Console.WriteLine ("WANRING: MULTIPLE CLAIMS TO THE SAME SUBDOMAIN DETECTED!");
					}
					subdomainDict [sub] = M;
				}
			}
			if (rootMandate == null) {
				Console.WriteLine ("WARNING: NO MANDATES CLAIMED ROOT!");
			}
		}
		protected void OnMandateRebuilt(Mandate source, List<string> buildLog) {
			Console.WriteLine ("Mandate \"{0}\" successfully rebuilt.", source.Filename);
			this.validationflag = true;
			this.Watchgate.Set ();
		}
		protected void OnMandateFailedRebuild(Mandate source, List<string> buildLog, Exception e) {
			Console.WriteLine ("Mandate \"{0}\" failed rebuild or validation:", source.Filename);
			Console.WriteLine (e);
			foreach(string line in buildLog) {
				Console.WriteLine (line);
			}
		}
		protected Site GetSite(ClientRequest CR) {
			try {
				//string[] domsplit = CR.Host.Split (new char[] {'.'}).Reverse().ToArray();
				Mandate M;
				if (subdomainDict.TryGetValue(CR.Subdomain, out M)) {
					return M.GetSite (CR);
				} else {
					if (rootMandate != null) {
						return rootMandate.GetSite (CR);
					} else {
						throw new Exception ("A request to the root domain was made, but none of your mandates claim root ownership.");
					}
				}
			} catch (Exception e) {
				Console.WriteLine (e);
				return null;
			}
		}
		public ClientReturn Get(ClientRequest CR) {
			return this.GetSite (CR).Get (CR);
		}
		public ClientReturn Post(ClientRequest CR, ClientBody CB) {
			return this.GetSite (CR).Post (CR, CB);
		}
		public bool IsAuthorized(ClientRequest CR, Tuple<string, string> auth, out string realm) {
			return this.GetSite (CR).IsAuthorized (CR, auth, out realm);
		}
		public CacheData GetCacheData(ClientRequest CR) {
			return this.GetSite (CR).GetCacheData (CR);
		}
	}
	public class Mandate {
		protected Module MODULE;
		protected FileInfo sbrFile;
		protected List<string> buildRefs = new List<string>();
		protected List<FileInfo> csFiles = new List<FileInfo>();
		protected Site[] SITES = new Site[0];
		protected Site rootSite;
		protected ConcurrentDictionary<string, Site> subdomainDict = new ConcurrentDictionary<string, Site>();
		protected CompilerResults resultsPrevious;
		public string[] Subdomains { get{return this.subdomainDict.Keys.ToArray ();} }
		public bool ClaimsRoot { get{return (rootSite != null);} }
		public string Filename {get {return this.sbrFile.Name;}}
		public string Name {get{return this.sbrFile.Name.Substring(0, this.sbrFile.Name.Length - 4);}}
		protected ManualResetEventSlim buildWait = new ManualResetEventSlim (true);
		public event MandateBuildSuccessHandler MandateBuildSuccess;
		public event MandateBuildFailureHandler MandateBuildFailure;
		public Mandate (string path) {
			sbrFile = new FileInfo (path);
			this.EvalSbr ();
			this.MandateBuildSuccess += OnBuildSuccess;
			this.MandateBuildFailure += OnBuildFailure;
			//this.Build ();
		}
		protected enum SbrLineState {NUL, REF, SRC}
		protected void EvalSbr() {
			try {
				buildRefs = new List<string> ();
				csFiles = new List<FileInfo> ();
				StreamReader sbr = sbrFile.OpenText ();
				SbrLineState ls = SbrLineState.NUL;
				foreach(string line in sbr.ReadToEnd().Split(new char[] {'\n'}).Where(l => l.Length > 0)) {
					if (line.Length >= 5) {
						switch (line.Substring (0, 5)) {
						case "[REF]":
							ls = SbrLineState.REF;
							continue;
						case "[SRC]":
							ls = SbrLineState.SRC;
							continue;
						}
					}
					switch(ls) {
						case SbrLineState.NUL:
						break;
						case SbrLineState.REF:
						this.buildRefs.Add(line);
						break;
						case SbrLineState.SRC:
						if (File.Exists(LocalSiteReference(line))) {
							this.csFiles.Add (new FileInfo(LocalSiteReference(line)));
						} else {
							Console.WriteLine("ERROR: File \"{0}\" does not exist as specified in mandate \"{1}\"", line, this.Filename);
						}
						break;
					}
				}
			} catch (Exception e) {
				Console.WriteLine (e);
			}
		}
		protected int watchtime = 500;
		protected bool watching = false;
		protected AutoResetEvent Watchgate = new AutoResetEvent(false);
		protected Thread Watchdog;
		public void Begin() {
			if (Watchdog == null) {
				watching = true;
				Watchdog = new Thread (Watch);
				Watchdog.Start ();
			}
		}
		protected void Watch () {
			while (watching) {
				Watchgate.WaitOne (watchtime);
				if (watching) {
					FileInfo sbrcheck = new FileInfo (sbrFile.FullName);
					if (sbrcheck.LastWriteTime != sbrFile.LastWriteTime) {
						sbrFile.Refresh ();
						this.EvalSbr ();
						this.Build ();
					}
					foreach(FileInfo cs in csFiles) {
						FileInfo cscheck = new FileInfo (cs.FullName);
						if (cscheck.LastWriteTime != cs.LastWriteTime) {
							cs.Refresh ();
							this.Build ();
						}
					}
				}
			}
		}
		public void End() {
			if (Watchdog != null) {
				watching = false;
				Watchgate.Set ();
				Watchdog.Join ();
				Watchdog = null;
			}
		}
		public bool Build() {
			buildWait.Reset();
			List<string> buildOut = new List<string> ();
			try {
				CompilerParameters compParam = new CompilerParameters ();
				compParam.GenerateInMemory = true;
				compParam.GenerateExecutable = false;
				compParam.TreatWarningsAsErrors = false;
				compParam.CompilerOptions = "/optimize";
				compParam.ReferencedAssemblies.AddRange (new string[] {"System.dll", "Lexicon.dll", "WebSharp.dll"});
				compParam.ReferencedAssemblies.AddRange (buildRefs.ToArray());
				CSharpCodeProvider provider = new CSharpCodeProvider ();
				resultsPrevious = provider.CompileAssemblyFromFile (compParam, csFiles.Select((fi) => fi.FullName).ToArray());
				foreach(string o in resultsPrevious.Output) {
					buildOut.Add(o);
				}
				if (resultsPrevious.Errors.HasErrors) {
					throw new Exception("Compilation of mandate failed.");
				}
				Module[] mods = resultsPrevious.CompiledAssembly.GetModules();
				if (mods.Length > 1) {
					throw new Exception("Compiled mandate has more than one module. (how did this even happen)");
				}
				Module mod = mods [0];
				IEnumerable<Type> sites = from site in mod.GetTypes () where site.BaseType == typeof(SiteBase) select site;
				if (sites.Count() == 0) {
					throw new Exception("Compiled mandate has no sites.");
				}
				foreach(object attr in mod.Assembly.GetCustomAttributes(true)) {
					if (attr.GetType() == typeof(RefreshTime)) {
						this.watchtime = ((RefreshTime)attr).msec;
					}
				}
				List<Site> siteList = new List<Site>();
				foreach(Type site in sites) {
					bool root = false;
					List<string> subdomains = new List<string>();
					foreach (object attr in site.GetCustomAttributes(true)) {
						if(attr.GetType() == typeof(Root)) {
							root = true;
						}
						if (attr.GetType() == typeof(Subdomains)) {
							subdomains.AddRange( ((Subdomains)attr).subdomains );
						}
					}
					Site fsite = new Site(site, root, subdomains.ToArray());
					siteList.Add(fsite);
				}
				foreach(Site S in this.SITES) {S.Upkeep();}
				this.SITES = siteList.ToArray();
				this.MODULE = mod;
				this.RepopulateDictionary();
				buildWait.Set();
				this.MandateBuildSuccess(this, buildOut);
				return true;
			} catch (Exception e) {
				this.MandateBuildFailure (this, buildOut, e);
				buildWait.Set();
				return false;
			}
		}
		protected void RepopulateDictionary() {
			rootSite = null;
			subdomainDict = new ConcurrentDictionary<string, Site> ();
			foreach(Site S in this.SITES) {
				if (S.root) {
					if (rootSite != null) {Console.WriteLine ("WARNING: MULTIPLE CLAIMS OF THE ROOT DOMAIN ON THE SAME MANDATE!");}
					this.rootSite = S;
				}
				foreach(string sub in S.subdomains) {
					if (subdomainDict.ContainsKey(sub)) {
						Console.WriteLine ("WARNING: MULTIPLE CLAIMS OF SUBDOMAIN \"{0}\" ON THE SAME MANDATE!", sub);
					}
					subdomainDict [sub] = S;
				}
			}
		}
		public string[] GetMostRecentBuildErrorLog () {
			string[] log = new string[resultsPrevious.Errors.Count];
			for (int i=0;i<resultsPrevious.Errors.Count;++i) {
				log [i] = resultsPrevious.Errors [i].ErrorText;
			}
			return log;
		}
		internal Site GetSite(ClientRequest CR) {
			try {
				Site g;
				buildWait.Wait ();
				if (subdomainDict.TryGetValue(CR.Subdomain, out g)) {
					return g;
				} else {
					if (rootSite != null) {
						return rootSite;
					} else {
						throw new Exception ("A request was made to a non-existent root site on this Mandate. This should never happen under any circumstance whatsoever, this message should only ever have been viewed in the source code.");
					}
				}
			} catch (Exception e) {
				Console.WriteLine (e);
				return null;
			}
		}
		protected virtual void OnBuildSuccess(Mandate source, List<string> buildLog) {
			if (File.Exists(LocalSiteReference(this.Name+".buildfailure.log"))) {
				File.Delete (LocalSiteReference(this.Name+".buildfailure.log"));
			}
		}
		protected virtual void OnBuildFailure(Mandate source, List<string> buildLog, Exception e) {
			Console.WriteLine (String.Format("A Build has failed, below is the build log and the exception:\n\nBuild Log: {0}\n\nException: {1}", String.Join("\n", buildLog), e));
			File.WriteAllText (LocalSiteReference(this.Name+".buildfailure.log"), String.Format("A Build has failed, below is the build log and the exception:\n\nBuild Log: {0}\n\nException: {1}", String.Join("\n", buildLog), e));
		}
		public static string LocalAssemblyReference(string dllName) {
			return Path.Combine (Environment.CurrentDirectory, dllName);
		}
		public static string LocalSiteReference(string dllName) {
			return Path.Combine (Environment.CurrentDirectory, "Sites", dllName);
		}
	}

	public class Site {
		public readonly bool root;
		public readonly string[] subdomains;
		protected Type modref;
		protected object instance;
		protected MethodInfo UPKEEP { get{return modref.GetMethod ("Upkeep");} }
		protected MethodInfo GET { get{return modref.GetMethod ("Get");} }
		protected MethodInfo POST { get{return modref.GetMethod ("Post");} }
		protected MethodInfo ISAUTH { get{return modref.GetMethod ("IsAuthorized");} }
		protected MethodInfo CACHE { get{return modref.GetMethod ("GetCacheData");} }
		protected bool booted = false;
		public bool IsBooted {get {return booted;}}
		public Site(Type modref, bool root, string[] subdomains) {
			Console.WriteLine ("Booting site {0}...", modref.Name);
			this.modref = modref;
			this.root = root;
			this.subdomains = subdomains;
			instance = modref.GetConstructor (new Type[0]).Invoke(new object[0]);
			this.booted = true;
			Console.WriteLine ("...Complete");
		}
		public void Upkeep () {
			UPKEEP.Invoke (instance, null);
		}
		public ClientReturn Get(ClientRequest CR) {
			return GET.Invoke (instance, new object[] {CR}) as ClientReturn;
		}
		public ClientReturn Post(ClientRequest CR, ClientBody CB) {
			return POST.Invoke (instance, new object[] {CR, CB}) as ClientReturn;
		}
		public bool IsAuthorized(ClientRequest CR, Tuple<string, string> auth, out string realm) {
			object[] param = new object[] { CR, auth, null };
			bool r = (bool)ISAUTH.Invoke (instance, param);
			realm = param [2] as string;
			return r;
		}
		public CacheData GetCacheData(ClientRequest CR) {
			return CACHE.Invoke (instance, new object[] {CR}) as CacheData;
		}
	}

	//EVENT
	public delegate void MandateBuildSuccessHandler(Mandate source, List<string> buildLog);
	public delegate void MandateBuildFailureHandler(Mandate source, List<string> buildLog, Exception e);
}

