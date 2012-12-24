using System;
using System.Collections.Generic;
using System.IO;
using D_Parser.Dom;
using D_Parser.Resolver.ASTScanner;
using System.Threading;
using D_Parser.Resolver;
using D_Parser.Resolver.TypeResolution;

namespace D_Parser.Misc
{
	/// <summary>
	/// Stores syntax trees while regarding their package hierarchy.
	/// </summary>
	public class ParseCache : IEnumerable<IAbstractSyntaxTree>, IEnumerable<ModulePackage>
	{
		#region Properties
		public bool IsParsing { get { return parseThread != null && parseThread.IsAlive; } }
		Thread parseThread;

		/// <summary>
		/// FIXME: Make a fileLookup-package constraint that enforces a module naming consistency
		/// </summary>
		Dictionary<string, IAbstractSyntaxTree> fileLookup = new Dictionary<string, IAbstractSyntaxTree>();
		public RootPackage Root = new RootPackage();

		public bool EnableUfcsCaching = true;
		/// <summary>
		/// The cache which holds resolution results of the global scope's methods' first parameters - used to increase completion performance
		/// </summary>
		public readonly UFCSCache UfcsCache = new UFCSCache();
		/// <summary>
		/// If a parse directory is relative, like ../ or similar, use this path as base path
		/// </summary>
		public string FallbackPath;
		/// <summary>
		/// Used to fill the $solution variable that may occur in include paths
		/// </summary>
		public string SolutionPath = string.Empty;
		public List<string> ParsedDirectories = new List<string>();

		public Exception LastParseException { get; private set; }

		public bool IsObjectClassDefined
		{
			get { return ObjectClass != null; }
		}

		/// <summary>
		/// To improve resolution performance, the object class that can be defined only once will be stored over here.
		/// </summary>
		public DClassLike ObjectClass
		{
			get;
			private set;
		}

		public AbstractType SizeT
		{
			get;
			private set;
		}

		/// <summary>
		/// See <see cref="ObjectClass"/>
		/// </summary>
		public ClassType ObjectClassResult
		{
			get;
			private set;
		}
		#endregion

		#region Parsing management
		public delegate void ParseFinishedHandler(ParsePerformanceData[] PerformanceData);
		public event ParseFinishedHandler FinishedParsing;
		public event Action FinishedUfcsCaching;

		public void BeginParse()
		{
			BeginParse(ParsedDirectories, FallbackPath);
		}

		/// <summary>
		/// Parses all directories and updates the cache contents
		/// </summary>
		public void BeginParse(IEnumerable<string> directoriesToParse, string fallbackAbsolutePath)
		{
			var performanceLogs = new List<ParsePerformanceData>();

			AbortParsing();

			// Clear all kinds of caches to free memory as soon as possible!
			UfcsCache.Clear();
			Clear(directoriesToParse == null);
			GC.Collect(); // Run a collection cycle to entirely free previously free'd memory


			FallbackPath = fallbackAbsolutePath;

			if (directoriesToParse == null)
			{
				if (FinishedParsing != null)
					FinishedParsing(null);
				return;
			}

			parseThread = new Thread(parseDg);

			parseThread.IsBackground = true;
			parseThread.Start(new Tuple<IEnumerable<string>, List<ParsePerformanceData>>(directoriesToParse, performanceLogs));
		}

		public void WaitForParserFinish()
		{
			if (parseThread != null && parseThread.IsAlive)
				parseThread.Join();
		}

		public void AbortParsing()
		{
			if (parseThread != null && parseThread.IsAlive)
				parseThread.Abort();
		}

		void parseDg(object o)
		{
			var tup = (Tuple<IEnumerable<string>, List<ParsePerformanceData>>)o;

			var parsedDirs = new List<string>();
			var newRoot = new RootPackage();
			foreach (var d in tup.Item1)
			{
				parsedDirs.Add(d);

				var dir = d.Replace("$solution", SolutionPath);
				if (!Path.IsPathRooted(dir))
					dir = Path.Combine(FallbackPath, dir);

				var ppd = ThreadedDirectoryParser.Parse(dir, newRoot);

				if (ppd != null)
					tup.Item2.Add(ppd);
			}

			UfcsCache.Clear();
			ParsedDirectories = parsedDirs;
			Root = newRoot;

			// For performance boost, pre-resolve the object class
			HandleObjectModule(GetModule("object"));

			if (FinishedParsing != null)
				FinishedParsing(tup.Item2.ToArray());

			if (EnableUfcsCaching)
			{
				UfcsCache.Update(ParseCacheList.Create(this));

				if (FinishedUfcsCaching != null)
					FinishedUfcsCaching();
			}
		}

		public bool UpdateRequired(string[] paths)
		{
			if (paths == null)
				return false;

			// If current dir count != the new dir count
			bool cacheUpdateRequired = paths.Length != ParsedDirectories.Count;

			// If there's a new directory in it
			if (!cacheUpdateRequired)
				foreach (var path in paths)
					if (!ParsedDirectories.Contains(path))
					{
						cacheUpdateRequired = true;
						break;
					}

			if (!cacheUpdateRequired && paths.Length != 0)
				cacheUpdateRequired =
					Root.Modules.Count == 0 &&
					Root.Packages.Count == 0;

			return cacheUpdateRequired;
		}

		public void Clear(bool parseDirectories = false)
		{
			Root = null;
			if (parseDirectories)
				ParsedDirectories = null;

			Root = new RootPackage();
		}

		void HandleObjectModule(IAbstractSyntaxTree objModule)
		{
			if (objModule != null)
				foreach (var m in objModule)
					if (m.Name == "Object" && m is DClassLike)
					{
						ObjectClassResult = new ClassType(ObjectClass = (DClassLike)m, new IdentifierDeclaration("Object"), null);
						break;
					}
					else if(m.Name == "size_t")
					{
						//TODO: Do a version check, so that only on x64 dmd installations, size_t equals ulong.
						SizeT = TypeDeclarationResolver.HandleNodeMatch(m, 
							ResolutionContext.Create(ParseCacheList.Create(this), null, objModule));
					}
		}
		#endregion

		#region Tree management
		/// <summary>
		/// Use this method to add a syntax tree to the parse cache.
		/// Equally-named trees will be overwritten. 
		/// </summary>
		public void AddOrUpdate(IAbstractSyntaxTree ast)
		{
			if (ast == null || string.IsNullOrEmpty(ast.ModuleName))
				return;

			if(!string.IsNullOrEmpty(ast.FileName))
				fileLookup[ast.FileName] = ast;
				
			var packName = ModuleNameHelper.ExtractPackageName(ast.ModuleName);

			if (string.IsNullOrEmpty(packName))
			{
				Root.Modules[ast.ModuleName] = ast;

				if (ast.ModuleName == "object")
					HandleObjectModule(ast);
				return;
			}

			var pack = Root.GetOrCreateSubPackage(packName, true);

			pack.Modules[ModuleNameHelper.ExtractModuleName(ast.ModuleName)] = ast;
		}

		/// <summary>
		/// Returns null if no module was found.
		/// </summary>
		public IAbstractSyntaxTree GetModule(string moduleName)
		{
			var packName = ModuleNameHelper.ExtractPackageName(moduleName);

			var pack = Root.GetOrCreateSubPackage(packName, false);

			if (pack != null)
			{
				IAbstractSyntaxTree ret = null;
				if (pack.Modules.TryGetValue(ModuleNameHelper.ExtractModuleName(moduleName), out ret))
					return ret;
			}

			return null;
		}

		public bool Remove(IAbstractSyntaxTree ast)
		{
			if (ast == null)
				return false;

			if (string.IsNullOrEmpty(ast.ModuleName))
				return Root.Modules.ContainsValue(ast) && Root.Modules.Remove("");

			if(!string.IsNullOrEmpty(ast.FileName))
				fileLookup.Remove(ast.FileName);
			
			return _remFromPack(Root, ast);
		}

		bool _remFromPack(ModulePackage pack, IAbstractSyntaxTree ast)
		{
			if(pack.Modules.ContainsValue(ast))
				foreach (var kv in pack.Modules)
					if (kv.Value == ast)
					{
						pack.Modules.Remove(kv.Key);
						return true;
					}

			foreach (var p in pack.Packages)
				if (_remFromPack(p.Value, ast))
					return true;

			return false;
		}

		public IAbstractSyntaxTree GetModuleByFileName(string file, string baseDirectory)
		{
			IAbstractSyntaxTree ast;
			if(fileLookup.TryGetValue(file, out ast))
				return ast;
			return GetModule(DModule.GetModuleName(baseDirectory, file));
		}

		public IAbstractSyntaxTree this[string ModuleName]
		{
			get
			{
				return GetModule(ModuleName);
			}
		}
		#endregion

		public IEnumerator<IAbstractSyntaxTree> GetEnumerator()
		{
			return Root.GetEnumerator();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		IEnumerator<ModulePackage> IEnumerable<ModulePackage>.GetEnumerator()
		{
			return ((IEnumerable<ModulePackage>)Root).GetEnumerator();
		}
	}

	public class ParsePerformanceData
	{
		public string BaseDirectory;
		public int AmountFiles = 0;

		/// <summary>
		/// Duration (in seconds)
		/// </summary>
		public double TotalDuration = 0.0;

		public double FileDuration
		{
			get
			{
				if (AmountFiles > 0)
					return TotalDuration / AmountFiles;
				return 0;
			}
		}
	}
}
