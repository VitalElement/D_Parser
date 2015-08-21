using System.Linq;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Dom.Statements;
using D_Parser.Resolver.TypeResolution;
using D_Parser.Resolver.ExpressionSemantics;
using System.Collections.Generic;

namespace D_Parser.Resolver.Templates
{
	partial class TemplateParameterDeduction
	{
		public bool Visit(TemplateTypeParameter p)
		{
			var arg = visiteeArguments.Pop();

			// if no argument given, try to handle default arguments
			if (arg == null)
			{
				if (p.Default == null)
					return false;
				
				// Do the same stuff like in AbstractVisitor, just don't allow pushing DMethods but always its parent + introduce templateparametersymbols in the current resolution scope
				using (ctxt.Push(DResolver.SearchBlockAt(ctxt.ScopedBlock.NodeRoot as IBlockNode, p.Default.Location), p.Default.Location))
				{
					var defaultTypeRes = TypeDeclarationResolver.ResolveSingle(p.Default, ctxt);
					return defaultTypeRes != null && Set(p, defaultTypeRes, 0);
				}
			}

			// If no spezialization given, assign argument immediately
			if (p.Specialization == null)
				return Set(p, arg, 0);

			if (TemplateInstanceHandler.IsNonFinalArgument(arg))
			{
				foreach (var tp in targetSymbolStore.Keys.ToList())
					if (targetSymbolStore[tp] == null)
						targetSymbolStore[tp] = new TemplateParameterSymbol(tp, null);

				return true;
			}

			// Induce stuff from specialization
			if (!CreateTypeParamInductionVisitor().Accept(p.Specialization, arg))
				return false;

			// Apply the entire argument to parameter p if there hasn't been no explicit association yet
			TemplateParameterSymbol tps;
			if (!targetSymbolStore.TryGetValue(p, out tps) || tps == null)
				targetSymbolStore[p] = new TemplateParameterSymbol(p, arg);

			return true;
		}

		protected virtual TypeParamInduction CreateTypeParamInductionVisitor()
		{
			return new TypeParamInduction(this);
		}

		protected class TypeParamInduction : TypeDeclarationVisitor<bool>
		{
			#region Properties
			protected ISemantic resolvedThingToInduceFrom;
			protected AbstractType resolvedTypeToInduceFrom
			{
				//HACK Ensure that no information gets lost by using this function 
				// -- getting a value but requiring an abstract type and just extract it from the value - is this correct behaviour?
				get{ return AbstractType.Get(resolvedThingToInduceFrom); }
			}
			protected readonly TemplateParameterDeduction deduction;
			#endregion

			#region Constructor
			public TypeParamInduction(TemplateParameterDeduction ded)
			{
				deduction = ded;
			}
			#endregion

			#region Helpers
			/// <summary>
			/// Returns true if <param name="parameterNameHash">parameterNameHash</param> is expected somewhere in the template parameter list.
			/// </summary>
			protected bool IsExpectingParameter(int parameterNameHash)
			{
				foreach (var kv in deduction.targetSymbolStore)
					if (kv.Key.NameHash == parameterNameHash)
						return true;
				return false;
			}

			/// <summary>
			/// Returns true if both template instance identifiers are matching each other or if the parameterSpeci
			/// </summary>
			bool CheckForTixIdentifierEquality(
				DNode[] expectedTemplateTypes, 
				INode controllee)
			{
				/*
				 * Note: This implementation is not 100% correct or defined in the D spec:
				 * class A(T){}
				 * class A(S:string) {}
				 * class C(U: A!W, W){ W item; }
				 * 
				 * C!(A!int) -- is possible
				 * C!(A!string) -- is not allowed somehow - because there are probably two 'matching' template types.
				 * (dmd version 2.060, August 2012)
				 * Testing is done in ResolutionTests.TemplateParamDeduction13()
				 */
				return expectedTemplateTypes != null && expectedTemplateTypes.Contains(controllee);
			}

			DNode[] ResolveTemplateInstanceId(TemplateInstanceExpression tix)
			{
				var ctxt = deduction.ctxt;

				/*
				 * Again a very unclear/buggy situation:
				 * When having a cascaded tix as parameter, it uses the left-most part (i.e. the inner most) of the typedeclaration construct.
				 * 
				 * class C(A!X.SubClass, X) {} can be instantiated via C!(A!int), but not via C!(A!int.SubClass) - totally confusing
				 * (dmd v2.060)
				 */
				if (tix.InnerDeclaration != null)
				{
					if (tix.InnerMost is TemplateInstanceExpression)
						tix = (TemplateInstanceExpression)tix.InnerMost;
					else
						return new DNode[0];
				}

				var optBackup = ctxt.CurrentContext.ContextDependentOptions;
				ctxt.CurrentContext.ContextDependentOptions = ResolutionOptions.DontResolveBaseClasses | ResolutionOptions.DontResolveBaseTypes;

				var initialResults = ExpressionTypeEvaluation.GetOverloads(tix, ctxt, null, false);

				var l = new List<DNode>();
				foreach (var res in initialResults)
					if (res is DSymbol)
						l.Add((res as DSymbol).Definition);

				ctxt.CurrentContext.ContextDependentOptions = optBackup;

				return l.ToArray();
			}

			static ITypeDeclaration ConvertToTypeDeclarationRoughly(IExpression p)
			{
				while(p is SurroundingParenthesesExpression)
					p = ((SurroundingParenthesesExpression)p).Expression;

				var id = p as IdentifierExpression;
				if (id != null)
				{
					if(id.IsIdentifier)
						return new IdentifierDeclaration(id.ValueStringHash) { Location = p.Location, EndLocation = p.EndLocation };
				}
				else if (p is TypeDeclarationExpression)
					return ((TypeDeclarationExpression)p).Declaration;
				return null;
			}
			#endregion

			public bool Accept(ITypeDeclaration td, ISemantic r)
			{
				if (td == null)
					return false;

				this.resolvedThingToInduceFrom = r;

				return td.Accept(this);
			}

			public virtual bool Visit(IdentifierDeclaration id)
			{
				var r = resolvedThingToInduceFrom;
				var deducee = DResolver.StripMemberSymbols(resolvedTypeToInduceFrom) as DSymbol;

				// Bottom-level reached
				if (id.InnerDeclaration == null && IsExpectingParameter(id.IdHash) && !id.ModuleScoped)
				{
					// Associate template param with r
					return deduction.Set(null, r, id.IdHash);
				}

				if (id.InnerDeclaration != null && deducee != null && deducee.Definition.NameHash == id.IdHash)
				{
					var physicalParentType = TypeDeclarationResolver.HandleNodeMatch(deducee.Definition.Parent, deduction.ctxt, null, id.InnerDeclaration);
					if (Accept(id.InnerDeclaration, physicalParentType))
					{
						if (IsExpectingParameter(id.IdHash))
							deduction.Set(null, deducee, id.IdHash);
						return true;
					}
				}

				// If not stand-alone identifier or is not required as template param, resolve the id and compare it against r
				var _r = TypeDeclarationResolver.ResolveSingle(id, deduction.ctxt);
				return _r != null && ResultComparer.IsImplicitlyConvertible(r,_r);
			}

			public bool Visit(DTokenDeclaration tk)
			{
				var pt = resolvedTypeToInduceFrom as PrimitiveType;
				return pt != null && ResultComparer.IsPrimitiveTypeImplicitlyConvertible(pt.TypeToken, tk.Token);
			}

			public bool Visit(ArrayDecl arrayDeclToCheckAgainst)
			{
				var argumentArrayType = DResolver.StripMemberSymbols(resolvedTypeToInduceFrom) as AssocArrayType;

				if (argumentArrayType == null)
					return false;

				// Handle key type
				var at = argumentArrayType as ArrayType;
				if((arrayDeclToCheckAgainst.ClampsEmpty == (at == null)) &&
					(at == null || !at.IsStaticArray || arrayDeclToCheckAgainst.KeyExpression == null))
					return false;

				bool result;

				if (arrayDeclToCheckAgainst.KeyExpression != null)
				{
					var x_param = arrayDeclToCheckAgainst.KeyExpression;

					while (x_param is SurroundingParenthesesExpression)
						x_param = ((SurroundingParenthesesExpression)x_param).Expression;

					/*
					 * This might be critical:
					 * the [n] part in class myClass(T:char[n], int n) {}
					 * will be seen as an identifier expression, not as an identifier declaration.
					 * So in the case the parameter expression is an identifier,
					 * test if it's part of the parameter list
					 */
					var id = x_param as IdentifierExpression;
					if (id != null && id.IsIdentifier && IsExpectingParameter(id.ValueStringHash))
					{ // Match int[5] into T[n],n - after deduction, n will be 5

						// If an expression (the usual case) has been passed as argument, evaluate its value, otherwise is its type already resolved.
						var finalArg = argumentArrayType is ArrayType ? (ISemantic)new PrimitiveValue((argumentArrayType as ArrayType).FixedLength) : argumentArrayType.KeyType;

						//TODO: Do a type convertability check between the param type and the given argument's type.
						// The affected parameter must also be a value parameter then, if an expression was given.

						// and handle it as if it was an identifier declaration..
						result = deduction.Set(null, finalArg, id.ValueStringHash); 
					}
					else if (argumentArrayType is ArrayType)
					{ // Match int[5] into T[5]
						// Just test for equality of the argument and parameter expression, e.g. if both param and arg are 123, the result will be true.
						result = SymbolValueComparer.IsEqual(Evaluation.EvaluateValue(arrayDeclToCheckAgainst.KeyExpression, deduction.ctxt), new PrimitiveValue((argumentArrayType as ArrayType).FixedLength));
					}
					else
						result = false;
				}
				else if (arrayDeclToCheckAgainst.KeyType != null)
				{
					// If the array we're passing to the decl check that is static (i.e. has a constant number as key 'type'),
					// pass that number instead of type 'int' to the check.
					if (argumentArrayType != null && at != null && at.IsStaticArray)
						result = Accept(arrayDeclToCheckAgainst.KeyType,
							new PrimitiveValue(at.FixedLength));
					else
						result = Accept(arrayDeclToCheckAgainst.KeyType, argumentArrayType.KeyType);
				}
				else
					result = true;

				// Handle inner type
				return result && Accept(arrayDeclToCheckAgainst.InnerDeclaration, argumentArrayType.Base);
			}

			public bool Visit(DelegateDeclaration d)
			{
				var dr = DResolver.StripMemberSymbols(resolvedTypeToInduceFrom) as DelegateType;

				// Delegate literals or other expressions are not allowed
				if(dr==null || dr.IsFunctionLiteral)
					return false;

				var dr_decl = (DelegateDeclaration)dr.delegateTypeBase;

				// Compare return types
				if (d.IsFunction != dr_decl.IsFunction ||
				   dr.ReturnType == null ||
				   !Accept(d.ReturnType, dr.ReturnType))
					return false;
				
				// If no delegate args expected, it's valid
				if ((d.Parameters == null || d.Parameters.Count == 0) &&
					dr_decl.Parameters == null || dr_decl.Parameters.Count == 0)
					return true;

				// If parameter counts unequal, return false
				else if (d.Parameters == null || dr_decl.Parameters == null || d.Parameters.Count != dr_decl.Parameters.Count)
					return false;

				// Compare & Evaluate each expected with given parameter
				var dr_paramEnum = dr_decl.Parameters.GetEnumerator();
				foreach (var p in d.Parameters)
				{
					// Compare attributes with each other
					if (p is DNode)
					{
						if (!(dr_paramEnum.Current is DNode))
							return false;

						var dn = (DNode)p;
						var dn_arg = (DNode)dr_paramEnum.Current;

						if ((dn.Attributes == null || dn.Attributes.Count == 0) &&
							(dn_arg.Attributes == null || dn_arg.Attributes.Count == 0))
							return true;

						else if (dn.Attributes == null || dn_arg.Attributes == null ||
							dn.Attributes.Count != dn_arg.Attributes.Count)
							return false;

						foreach (var attr in dn.Attributes)
						{
							if(!dn_arg.ContainsAttribute(attr))
								return false;
						}
					}

					// Compare types
					if (p.Type!=null && dr_paramEnum.MoveNext() && dr_paramEnum.Current.Type!=null)
					{
						var dr_resolvedParamType = TypeDeclarationResolver.ResolveSingle(dr_paramEnum.Current.Type, deduction.ctxt);

						if (dr_resolvedParamType == null  ||
							!Accept(p.Type, dr_resolvedParamType))
							return false;
					}
					else
						return false;
				}
				return true;
			}

			public bool Visit(PointerDecl td)
			{
				var pointerType = DResolver.StripMemberSymbols(resolvedTypeToInduceFrom) as PointerType;
				return pointerType != null && Accept(td.InnerDeclaration, pointerType.Base);
			}

			public bool Visit(MemberFunctionAttributeDecl m)
			{
				var r = resolvedTypeToInduceFrom;

				if (r == null || r.Modifier == 0)
					return false;

				// Modifiers must be equal on both sides
				if (m.Modifier != r.Modifier)
					return false;

				// Strip modifier, but: immutable(int[]) becomes immutable(int)[] ?!
				AbstractType newR;
				if (r is AssocArrayType)
				{
					var aa = r as AssocArrayType;
					var clonedValueType = aa.Modifier != r.Modifier ? aa.ValueType.Clone(false) : aa.ValueType;

					clonedValueType.Modifier = r.Modifier;

					var at = aa as ArrayType;
					if (at != null)
						newR = at.IsStaticArray ? new ArrayType(clonedValueType, at.FixedLength) : new ArrayType(clonedValueType);
					else
						newR = new AssocArrayType(clonedValueType, aa.KeyType);
				}
				else
				{
					newR = r.Clone(false);
					newR.Modifier = 0;
				}

				// Now compare the type inside the parentheses with the given type 'r'
				return m.InnerType != null && Accept(m.InnerType, newR);
			}

			public bool Visit(TypeOfDeclaration t)
			{
				// Can I enter some template parameter referencing id into a typeof specialization!?
				// class Foo(T:typeof(1)) {} ?
				var t_res = TypeDeclarationResolver.ResolveSingle(t,deduction.ctxt);

				if (t_res == null)
					return false;

				return ResultComparer.IsImplicitlyConvertible(resolvedTypeToInduceFrom, t_res);
			}

			public bool Visit(VectorDeclaration td)
			{
				throw new System.NotImplementedException (); //TODO: Reimplement typedeclarationresolver as proper Visitor.
				/*if (r.DeclarationOrExpressionBase is VectorDeclaration)
				{
					var v_res = ExpressionTypeEvaluation.EvaluateType(v.Id, ctxt);
					var r_res = ExpressionTypeEvaluation.EvaluateType(((VectorDeclaration)r.DeclarationOrExpressionBase).Id, ctxt);

					if (v_res == null || r_res == null)
						return false;
					else
						return ResultComparer.IsImplicitlyConvertible(r_res, v_res);
				}*/
			}

			public bool Visit(VarArgDecl td)
			{
				throw new System.NotImplementedException();
			}

			public bool Visit(TemplateInstanceExpression tix)
			{
				/*
				 * TODO: Scan down r for having at least one templateinstanceexpression as declaration base.
				 * If a tix was found, check if the definition of the respective result base level 
				 * and the un-aliased identifier of the 'tix' parameter match.
				 * Attention: if the alias represents an undeduced type (i.e. a type bundle of equally named type nodes),
				 * it is only important that the definition is inside this bundle.
				 * Therefore, it's needed to manually resolve the identifier, and look out for aliases or such unprecise aliases..confusing as s**t!
				 * 
				 * If the param tix id is part of the template param list, the behaviour is currently undefined! - so instantly return false, I'll leave it as TODO/FIXME
				 */
				var paramTix_TemplateMatchPossibilities = ResolveTemplateInstanceId(tix);
				TemplateIntermediateType tixBasedArgumentType = null;
				var r_ = resolvedTypeToInduceFrom as DSymbol;
				while (r_ != null)
				{
					tixBasedArgumentType = r_ as TemplateIntermediateType;
					if (tixBasedArgumentType != null && CheckForTixIdentifierEquality(paramTix_TemplateMatchPossibilities, tixBasedArgumentType.Definition))
						break;

					r_ = r_.Base as DSymbol;
				}

				if (tixBasedArgumentType == null)
					return false;

				/*
				 * This part is very tricky:
				 * I still dunno what is allowed over here--
				 * 
				 * class Foo(T:Bar!E[],E) {} // (when deducing, parameter=T; tix=Bar!E[]; r=DerivateBar-ClassType
				 * ...
				 * Foo!(Bar!string[]) f; -- E will be 'string' then
				 * 
				 * class DerivateBar : Bar!string[] {} -- new Foo!DerivateBar() is also allowed, but now DerivateBar
				 *		obviously is not a template instance expression - it's a normal identifier only. 
				 */
				var argEnum_given = tixBasedArgumentType.Definition.TemplateParameters.GetEnumerator ();

				foreach (var p in tix.Arguments)
				{
					if (!argEnum_given.MoveNext() || argEnum_given.Current == null)
						return false;

					// Convert p to type declaration
					var param_Expected = ConvertToTypeDeclarationRoughly(p);

					if (param_Expected == null)
						return false;

					var result_Given = tixBasedArgumentType.DeducedTypes.FirstOrDefault ((tps) => tps.Parameter == argEnum_given.Current);

					if (result_Given == null || result_Given.Base == null || !Accept(param_Expected, result_Given.Base))
						return false;
				}

				// Too many params passed..
				return !argEnum_given.MoveNext();
			}
		}
	}
}
