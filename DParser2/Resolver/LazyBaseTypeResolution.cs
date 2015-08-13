using System;
using D_Parser.Misc;
using D_Parser.Dom;

namespace D_Parser.Resolver
{
	class LazyBaseTypeResolution
	{
		public enum State{
			NotResolvedYet,
			ExpectingResolution,
			NotExpectingResolution,
		}

		public State Status
		{
			get;
			protected set;
		}

		public LazyBaseTypeResolution (ResolutionContext ctxt, ISyntaxRegion thingToResolveLazily)
		{
			
		}
	}
}

