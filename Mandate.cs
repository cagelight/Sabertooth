using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;

using Sabertooth.Lexicon;
using Sabertooth.Lexicon.Attributes;

namespace Sabertooth.Mandate {
	public class MandateManager {
		protected HashSet<Mandate> mandates;
		protected Dictionary<string, Mandate> subdomainDict;
		protected Mandate rootMandate;
		public MandateManager() { 
			this.RebuildMandates ();
			this.RebuildReferences ();
		}
		public void RebuildMandates() {
			mandates = new HashSet<Mandate> ();
			foreach(string path in Directory.GetFiles(Path.Combine(Environment.CurrentDirectory, "Sites"), "*.cs", SearchOption.TopDirectoryOnly)) {
				mandates.Add(new Mandate (path));
			}
		}
		public void RebuildReferences() {
			subdomainDict = new Dictionary<string, Mandate> ();
			rootMandate = null;
			foreach(Mandate M in mandates) {
				if (M.ClaimsRoot) {
					if (rootMandate != null) {Console.WriteLine ("WARNING: MULTIPLE CLAIMS TO ROOT DETECTED!");}
					rootMandate = M;
				}
				foreach(string sub in M.Subdomains) {
					if (subdomainDict.ContainsKey(sub)) {Console.WriteLine ("WANRING: MULTIPLE CLAIMS TO THE SAME SUBDOMAIN DETECTED!");}
					subdomainDict [sub] = M;
				}
			}
			if (rootMandate == null) {
				Console.WriteLine ("WARNING: NO MANDATES CLAIMED ROOT!");
			}
		}
		public IStreamableContent Get(ClientRequest CR) {
			string[] domsplit = CR.Host.Split (new char[] {'.'}).Reverse().ToArray();
			Mandate M;
			if (domsplit.Length > 2 && subdomainDict.TryGetValue(domsplit[2], out M)) {
				return M.Get (CR, domsplit [2]);
			} else {
				if (rootMandate != null) {
					return rootMandate.Get (CR, null);
				} else {
					throw new Exception ("A request to the root domain was made, but none of your mandates claim root ownership.");
				}
			}
		}
	}
	public class Mandate {
		protected Module MODULE;
		protected FileInfo csFile;
		protected Site[] SITES = new Site[0];
		protected Site rootSite;
		protected Dictionary<string, Site> subdomainDict;
		protected CompilerResults resultsPrevious;
		public string[] Subdomains { get{return this.subdomainDict.Keys.ToArray ();} }
		public bool ClaimsRoot { get{return (rootSite != null);} }
		public Mandate (string path) {
			csFile = new FileInfo (path);
			this.Build ();
			this.RepopulateDictionary();
		}
		public bool Build() {
			try {
				CompilerParameters compParam = new CompilerParameters ();
				compParam.GenerateInMemory = true;
				compParam.GenerateExecutable = false;
				compParam.TreatWarningsAsErrors = false;
				compParam.CompilerOptions = "/optimize";
				compParam.ReferencedAssemblies.AddRange (new string[] {"System.dll", LocalAssemblyReference("Lexicon.dll"), "WebSharp.dll"});
				CSharpCodeProvider provider = new CSharpCodeProvider ();
				StreamReader csStream = csFile.OpenText ();
				resultsPrevious = provider.CompileAssemblyFromSource (compParam, csStream.ReadToEnd());
				csStream.Dispose ();
				if (resultsPrevious.Errors.HasErrors) {
					foreach(CompilerError E in resultsPrevious.Errors) {
						Console.WriteLine(E);
					}
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
				this.SITES = siteList.ToArray();
				this.MODULE = mod;
				return true;
			} catch (Exception e) {
				Console.WriteLine (e);
				return false;
			}
		}
		protected void RepopulateDictionary() {
			rootSite = null;
			subdomainDict = new Dictionary<string, Site> ();
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
		public IStreamableContent Get(ClientRequest CR, string subdomain) {
			Site g;
			if (subdomain != null && subdomainDict.TryGetValue(subdomain, out g)) {
				return g.Get (CR);
			} else {
				if (rootSite != null) {
					return rootSite.Get (CR);
				} else {
					throw new Exception ("A request was made to a non-existent root site on this Mandate. This should never happen under any circumstance whatsoever, this message should only ever have been viewed in the source code.");
				}
			}
		}
		public static string LocalAssemblyReference(string dllName) {
			return Path.Combine (Environment.CurrentDirectory, dllName);
		}
	}

	public class Site {
		public readonly bool root;
		public readonly string[] subdomains;
		protected Type modref;
		protected object instance;
		protected MethodInfo GET { get{return modref.GetMethod ("Get");} }
		public Site(Type modref, bool root, string[] subdomains) {
			this.modref = modref;
			this.root = root;
			this.subdomains = subdomains;
			instance = modref.GetConstructor (new Type[0]).Invoke(new object[0]);
		}
		public IStreamableContent Get(ClientRequest CR) {
			return GET.Invoke (instance, new object[] {CR}) as IStreamableContent;
		}
	}
}

