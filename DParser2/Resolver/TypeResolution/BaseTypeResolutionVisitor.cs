using System;
using D_Parser.Misc;
using D_Parser.Dom;

namespace D_Parser.Resolver.TypeResolution
{
	class BaseTypeResolutionVisitor : IResolvedTypeVisitor<AbstractType>
	{
		#region Properties
		public readonly Parameters parameters;
		public bool setResolvedSymbolNonStaticAccess;
		#endregion

		#region Helpers
		public class Wrapper
		{
			AbstractType baseType;
			BaseTypeResolutionVisitor lazyResolution;
			public bool NonStaticAccess
			{
				set
				{
					if (lazyResolution != null)
						lazyResolution.setResolvedSymbolNonStaticAccess = value;

					if (baseType != null)
						baseType.NonStaticAccess = value;
				}
			}

			public Wrapper(AbstractType t)
			{
				var adapter = t as AdapterDType;
				if (adapter != null)
					lazyResolution = adapter.ExistingResolution ?? new BaseTypeResolutionVisitor(adapter);
				else
					baseType = t;
			}

			public AbstractType ResolveOrGet()
			{
				if (baseType != null)
					return baseType;

				if (lazyResolution != null)
					baseType = lazyResolution.Resolve();

				return baseType;
			}

			public AbstractType GetWithoutResolution()
			{
				return baseType;
			}

			public AbstractType Clone(bool cloneInsteadReReference)
			{
				if (baseType != null)
					return cloneInsteadReReference ? baseType.Clone(true) : baseType;

				return cloneInsteadReReference ? new AdapterDType(lazyResolution.parameters) : new AdapterDType(lazyResolution);
			}

			public static implicit operator AbstractType(Wrapper w)
			{
				return w.ResolveOrGet();
			}

			public static implicit operator Wrapper(AbstractType t)
			{
				return new Wrapper(t);
			}
		}

		/// <summary>
		/// Pseudo-resolved Type that is used to pass parameters for lazy symbol resolution from the place of Type-Instantiation to DerivedDataType's constructor.
		/// </summary>
		public class AdapterDType : AbstractType
		{
			public const string lazyExcuse = "Don't use AdapterDType for non-internal resolution operations";
			public readonly Parameters Parameters;
			public readonly BaseTypeResolutionVisitor ExistingResolution;

			public AdapterDType(BaseTypeResolutionVisitor existingResolution) : this(existingResolution.parameters)
			{
				this.ExistingResolution = existingResolution;
			}

			public AdapterDType(Parameters p)
			{
				Parameters = p;
			}

			public override void Accept(IResolvedTypeVisitor vis)
			{
				throw new InvalidOperationException(lazyExcuse);
			}

			public override R Accept<R>(IResolvedTypeVisitor<R> vis)
			{
				throw new InvalidOperationException(lazyExcuse);
			}

			public override AbstractType Clone(bool cloneBase)
			{
				throw new InvalidOperationException(lazyExcuse);
			}
		}
		#endregion

		public class Parameters
		{
			public Parameters(ResolutionContext ctxt, ISyntaxRegion thingToResolveLazily)
			{

			}
		}

		BaseTypeResolutionVisitor(AdapterDType adapter) : this(adapter.Parameters)
		{
		}

		BaseTypeResolutionVisitor(Parameters p)
		{

		}



		public AbstractType Resolve()
		{
			return null;
		}



		#region Visitor implementation
		public AbstractType VisitAliasedType(AliasedType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitAmbigousType(AmbiguousType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitArrayAccessSymbol(ArrayAccessSymbol t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitArrayType(ArrayType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitAssocArrayType(AssocArrayType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitClassType(ClassType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitDelegateCallSymbol(DelegateCallSymbol t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitDelegateType(DelegateType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitDTuple(DTuple t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitEnumType(EnumType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitEponymousTemplateType(EponymousTemplateType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitInterfaceType(InterfaceType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitMemberSymbol(MemberSymbol t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitMixinTemplateType(MixinTemplateType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitModuleSymbol(ModuleSymbol t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitPackageSymbol(PackageSymbol t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitPointerType(PointerType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitPrimitiveType(PrimitiveType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitStaticProperty(StaticProperty t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitStructType(StructType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitTemplateParameterSymbol(TemplateParameterSymbol t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitTemplateType(TemplateType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitUnionType(UnionType t)
		{
			throw new NotImplementedException();
		}

		public AbstractType VisitUnknownType(UnknownType t)
		{
			throw new NotImplementedException();
		}
		#endregion
	}
}

