using System.Collections.Generic;
using D_Parser.Dom;
using System;
using D_Parser.Resolver.ExpressionSemantics;
using D_Parser.Resolver.TypeResolution;

namespace D_Parser.Resolver.Templates
{
	public partial class TemplateParameterDeduction : TemplateParameterVisitor<bool>
	{
		#region Properties

		/// <summary>
		/// The dictionary which stores all deduced results + their names
		/// </summary>
		protected readonly DeducedTypeDictionary targetSymbolStore;

		protected readonly Stack<ISemantic> visiteeArguments = new Stack<ISemantic>();

		/// <summary>
		/// Needed for resolving default types
		/// </summary>
		protected readonly ResolutionContext ctxt;

		#endregion

		#region Constructor / IO

		protected TemplateParameterDeduction(DeducedTypeDictionary DeducedParameters, ResolutionContext ctxt)
		{
			this.ctxt = ctxt;
			this.targetSymbolStore = DeducedParameters;
		}

		public static bool Deduce(TemplateParameter parameter, ISemantic thingToAssign, DeducedTypeDictionary targetSymbolStore, ResolutionContext ctxt)
		{
			return (new TemplateParameterDeduction(targetSymbolStore, ctxt)).Deduce(parameter, thingToAssign);
		}

		protected bool Deduce(TemplateParameter parameter, ISemantic thingToAssign)
		{
			// Packages aren't allowed at all
			if (thingToAssign is PackageSymbol)
				return false;

			// Module symbols can be used as alias only
			if (thingToAssign is ModuleSymbol &&
			    !(parameter is TemplateAliasParameter))
				return false;

			//TODO: Handle __FILE__ and __LINE__ correctly - so don't evaluate them at the template declaration but at the point of instantiation

			/*
			 * Introduce previously deduced parameters into current resolution context
			 * to allow value parameter to be of e.g. type T whereas T is already set somewhere before 
			 */
			DeducedTypeDictionary _prefLocalsBackup = null;
			if (ctxt != null && ctxt.CurrentContext != null)
			{
				_prefLocalsBackup = ctxt.CurrentContext.DeducedTemplateParameters;

				var d = new DeducedTypeDictionary();
				foreach (var kv in targetSymbolStore)
					if (kv.Value != null)
						d[kv.Key] = kv.Value;
				ctxt.CurrentContext.DeducedTemplateParameters = d;
			}

			visiteeArguments.Push(thingToAssign);
			bool deductionSuccessful = parameter.Accept(this);

			if (ctxt != null && ctxt.CurrentContext != null)
				ctxt.CurrentContext.DeducedTemplateParameters = _prefLocalsBackup;

			return deductionSuccessful;
		}

		#endregion

		public bool Visit(TemplateThisParameter tp)
		{
			// Only special handling required for method calls
			return tp.FollowParameter.Accept(this);
		}

		protected bool TrySetDefaultExpressionValue(TemplateValueParameter p)
		{
			if (p.DefaultExpression != null)
			{
				var eval = Evaluation.EvaluateValue(p.DefaultExpression, ctxt);

				if (eval == null)
					return false;

				return Set(p, eval, 0);
			}
			else
				return false;
		}

		public bool Visit(TemplateValueParameter p)
		{
			var arg = visiteeArguments.Pop();

			if (arg == null)
				return TrySetDefaultExpressionValue(p);

			var valueArgument = arg as ISymbolValue;

			// There must be a constant expression given!
			if (valueArgument == null)
				return false;

			// Check for param type <-> arg expression type match
			var paramType = TypeDeclarationResolver.ResolveSingle(p.Type, ctxt);

			if (paramType == null ||
			    valueArgument.RepresentedType == null ||
			    !ResultComparer.IsImplicitlyConvertible(paramType, valueArgument.RepresentedType))
				return false;

			// If spec given, test for equality (only ?)
			if (p.SpecializationExpression != null)
			{
				var specVal = Evaluation.EvaluateValue(p.SpecializationExpression, ctxt);

				if (specVal == null || !SymbolValueComparer.IsEqual(specVal, valueArgument))
					return false;
			}

			return Set(p, arg, 0);
		}

		public bool Visit(TemplateAliasParameter p)
		{
			var arg = visiteeArguments.Pop();

			if (arg == null)
			{
				if (p.DefaultType != null)
				{
					var res = TypeDeclarationResolver.ResolveSingle(p.DefaultType, ctxt);
					return res != null && Set(p, res, 0);
				}
				else
					return TrySetDefaultExpressionValue(p);
			}

			// Given argument must be a symbol - so no built-in type but a reference to a node or an expression
			var t = AbstractType.Get(arg);

			if (t == null)
				return false;

			if (!(t is DSymbol))
			{
				while (t != null)
				{
					if (t is PrimitiveType) // arg must not base on a primitive type.
						return false;

					if (t is DerivedDataType)
						t = ((DerivedDataType)t).Base;
					else
						break;
				}
			}

			if (p.SpecializationExpression != null)
			{
				// LANGUAGE ISSUE: Can't do anything here - dmd won't let you use MyClass!(2) though you have class MyClass(alias X:2)
				return false;
			}
			else if (p.SpecializationType != null)
			{
				// ditto
				return false;
			}

			return Set(p, arg, 0);
		}

		public bool Visit(TemplateTupleParameter tp)
		{
			var l = new List<ISemantic>();

			var current = new List<ISemantic>();
			var next = new List<ISemantic>();

			current.Add(visiteeArguments.Pop());

			while (current.Count != 0)
			{
				foreach (var i in current)
				{
					var tuple = i as DTuple;
					if (tuple != null) // If a type tuple was given already, add its items instead of the tuple itself
					{
						if (tuple.Items != null)
							next.AddRange(tuple.Items);
					}
					else if (i != null)
						l.Add(i);
				}

				current.Clear();
				current.AddRange(next);
				next.Clear();
			}

			return Set(tp, new DTuple(l.Count == 0 ? null : l), 0);
		}

		/// <summary>
		/// Returns false if the item has already been set before and if the already set item is not equal to 'r'.
		/// Inserts 'r' into the target dictionary and returns true otherwise.
		/// </summary>
		protected bool Set(TemplateParameter p, ISemantic r, int nameHash)
		{
			if (p == null)
			{
				if (nameHash != 0 && targetSymbolStore.ExpectedParameters != null)
				{
					foreach (var tpar in targetSymbolStore.ExpectedParameters)
						if (tpar.NameHash == nameHash)
						{
							p = tpar;
							break;
						}
				}
			}

			if (p == null)
			{
				ctxt.LogError(null, "no fitting template parameter found!");
				return false;
			}

			// void call(T)(T t) {}
			// call(myA) -- T is *not* myA but A, so only assign myA's type to T. 
			if (p is TemplateTypeParameter)
			{
				var newR = Resolver.TypeResolution.DResolver.StripMemberSymbols(AbstractType.Get(r));
				if (newR != null)
					r = newR;
			}

			TemplateParameterSymbol rl;
			if (!targetSymbolStore.TryGetValue(p, out rl) || rl == null)
			{
				targetSymbolStore[p] = new TemplateParameterSymbol(p, r);
				return true;
			}
			else
			{
				if (ResultComparer.IsEqual(rl.Base, r))
				{
					targetSymbolStore[p] = new TemplateParameterSymbol(p, r);
					return true;
				}
				else if (rl == null)
					targetSymbolStore[p] = new TemplateParameterSymbol(p, r);

				// Error: Ambiguous assignment

				return false;
			}
		}
	}
}
