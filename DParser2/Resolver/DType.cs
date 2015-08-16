using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using D_Parser.Dom;
using D_Parser.Dom.Expressions;
using D_Parser.Parser;
using D_Parser.Resolver.ExpressionSemantics;
using System.Text;
using D_Parser.Resolver.TypeResolution;

namespace D_Parser.Resolver
{
	public abstract class AbstractType : ISemantic, IVisitable<IResolvedTypeVisitor>
	{
		#region Properties
		Dictionary<string, object> tags;
		public void Tag(string id, object tag) {
			if (tags == null)
				tags = new Dictionary<string, object> ();
			tags [id] = tag;
		}
		public T Tag<T>(string id) where T : class{
			object o;
			if (tags == null || !tags.TryGetValue (id, out o))
				return default(T);
			return (T)o;
		}

		public void AssignTagsFrom(AbstractType t)
		{
			if (t.tags == null)
				tags = null;
			else
				tags = new Dictionary<string, object> (t.tags);
		}

		public virtual bool NonStaticAccess { get; set; }

		/// <summary>
		/// e.g. const, immutable
		/// </summary>
		public virtual byte Modifier {
			get;
			set;
		}
		#endregion

		#region Constructor/Init
		protected AbstractType() { }
		#endregion

		public override string ToString()
		{
			return ToCode(true);
		}

		public string ToCode()
		{
			return DTypeToCodeVisitor.GenerateCode(this);
		}

		public string ToCode(bool pretty)
		{
			return DTypeToCodeVisitor.GenerateCode(this, pretty);
		}

		public static AbstractType Get(ISemantic s)
		{
			//FIXME: What to do with the other overloads?
			if (s is InternalOverloadValue)
				return new AmbiguousType((s as InternalOverloadValue).Overloads);
			if (s is ISymbolValue)
				return (s as ISymbolValue).RepresentedType;
			
			return s as AbstractType;
		}

		public static AbstractType[] Get<R>(IEnumerable<R> at)
			where R : class,ISemantic
		{
			var l = new List<AbstractType>();

			if (at != null)
				foreach (var t in at)
				{
					if (t is AbstractType)
						l.Add(t as AbstractType);
					else if (t is ISymbolValue)
						l.Add(((ISymbolValue)t).RepresentedType);
				}

			return l.ToArray();
		}

		public abstract AbstractType Clone(bool cloneBase);

		public abstract void Accept(IResolvedTypeVisitor vis);
		public abstract R Accept<R>(IResolvedTypeVisitor<R> vis);
	}

	#region Special types
	public class UnknownType : AbstractType
	{
		public readonly ISyntaxRegion BaseExpression;
		public UnknownType(ISyntaxRegion BaseExpression) {
			this.BaseExpression = BaseExpression;
		}

		public override void Accept (IResolvedTypeVisitor vis)
		{
			vis.VisitUnknownType (this);
		}

		public override R Accept<R> (IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitUnknownType (this);
		}

		public override AbstractType Clone (bool cloneBase)
		{
			return new UnknownType (BaseExpression);
		}
	}

	public class AmbiguousType : AbstractType
	{
		public readonly AbstractType[] Overloads;

		public override bool NonStaticAccess
		{
			get
			{
				return base.NonStaticAccess;
			}
			set
			{
				base.NonStaticAccess = value;
				foreach (var o in Overloads)
					o.NonStaticAccess = value;
			}
		}

		public static AbstractType Get(IEnumerable<AbstractType> types)
		{
			if (types == null)
				return null;
			var en = types.GetEnumerator();
			if (!en.MoveNext())
				return null;
			var first = en.Current;
			if (!en.MoveNext())
				return first;
			en.Dispose();

			return new AmbiguousType(types);
		}

		public static IEnumerable<AbstractType> TryDissolve(AbstractType t)
		{
			if (t is AmbiguousType)
			{
				foreach (var o in (t as AmbiguousType).Overloads)
					yield return o;
			}
			else if (t != null)
				yield return t;
		}

		public override byte Modifier
		{
			get
			{
				if (Overloads.Length != 0)
					return Overloads[0].Modifier;
				return base.Modifier;
			}
			set
			{
				foreach (var ov in Overloads) {
					ov.Modifier = value;
					DResolver.StripMemberSymbols (ov).Modifier = value;
				}

				base.Modifier = value;
			}
		}

		public AmbiguousType(IEnumerable<AbstractType> o)
		{
			if (o == null)
				throw new ArgumentNullException("o");

			var l = new List<AbstractType>();
			foreach (var ov in o)
				if (ov != null)
					l.Add(ov);
			Overloads = l.ToArray();
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new AmbiguousType(Overloads);
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitAmbigousType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitAmbigousType(this);
		}
	}
	#endregion

	public class PrimitiveType : AbstractType
	{
		public readonly byte TypeToken;

		public PrimitiveType(byte TypeToken, byte Modifier = 0)
		{
			this.TypeToken = TypeToken;
			this.Modifier = Modifier;
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new PrimitiveType(TypeToken, Modifier);
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitPrimitiveType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitPrimitiveType(this);
		}
	}

	#region Derived data types
	public abstract class DerivedDataType : AbstractType
	{
		internal readonly BaseTypeResolutionVisitor.Wrapper baseType;

		public virtual AbstractType Base
		{
			get {
				return baseType;
			}
		}

		protected DerivedDataType(AbstractType @base, bool setNonStaticAccess = false)
		{
			baseType = @base;
			baseType.NonStaticAccess = setNonStaticAccess;
		}

		protected virtual AbstractType CloneBase(bool cloneBase)
		{
			return baseType.Clone(cloneBase);
		}
	}

	public class PointerType : DerivedDataType
	{
		public PointerType(AbstractType Base) : base(Base) { }

		public override AbstractType Clone(bool cloneBase)
		{
			return new PointerType(CloneBase(cloneBase));
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitPointerType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitPointerType(this);
		}
	}

	public class ArrayType : AssocArrayType
	{
		public readonly int FixedLength;
		public readonly bool IsStaticArray;

		public ArrayType(AbstractType ValueType)
			: base(ValueType, null) { FixedLength = -1; }

		public ArrayType(AbstractType ValueType, int ArrayLength)
			: base(ValueType, null)
		{
			FixedLength = ArrayLength;
			IsStaticArray = ArrayLength >= 0;
		}

		public override AbstractType Clone(bool cloneBase)
		{
			if(IsStaticArray)
				return new ArrayType(CloneBase(cloneBase));
			
			return new ArrayType(CloneBase(cloneBase), FixedLength);
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitArrayType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitArrayType(this);
		}
	}

	public class AssocArrayType : DerivedDataType
	{
		readonly BaseTypeResolutionVisitor.Wrapper keyType;
		public AbstractType KeyType
		{
			get { return keyType; }
		}

		public bool IsString
		{
			get{
				var pt = DResolver.StripMemberSymbols(ValueType) as PrimitiveType;
				return this is ArrayType && pt != null && DTokens.IsBasicType_Character(pt.TypeToken);
			}
		}

		/// <summary>
		/// Aliases <see cref="Base"/>
		/// </summary>
		public AbstractType ValueType { get { return Base; } }

		public AssocArrayType(AbstractType ValueType, AbstractType KeyType)
			: base(ValueType, true)
		{
			this.keyType = new BaseTypeResolutionVisitor.Wrapper(KeyType);
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new AssocArrayType(CloneBase(cloneBase), keyType.Clone(cloneBase));
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitAssocArrayType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitAssocArrayType(this);
		}
	}

	/// <summary>
	/// Represents calling a delegate. 
	/// Used to determine whether a delegate was called or just has been referenced.
	/// </summary>
	public class DelegateCallSymbol : DerivedDataType
	{
		public readonly DelegateType Delegate;
		internal readonly PostfixExpression_MethodCall callExpression;

		public DelegateCallSymbol (DelegateType dg, PostfixExpression_MethodCall callExpression) : base (dg.Base)
		{
			this.Delegate = dg;
			this.callExpression = callExpression;
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new DelegateCallSymbol(cloneBase && Delegate != null ? Delegate.Clone(true) as DelegateType : Delegate, callExpression);
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitDelegateCallSymbol(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitDelegateCallSymbol(this);
		}
	}

	public class DelegateType : DerivedDataType
	{
		public readonly bool IsFunction;
		public readonly ISyntaxRegion delegateTypeBase;
		public bool IsFunctionLiteral { get { return delegateTypeBase is FunctionLiteral; } }
		readonly BaseTypeResolutionVisitor.Wrapper[] parameters;
		public IEnumerable<AbstractType> Parameters {
			get
			{
				foreach (var p in parameters)
					yield return p;
			}
		}

		public DelegateType(AbstractType ReturnType,DelegateDeclaration Declaration, IEnumerable<AbstractType> Parameters = null) : base(ReturnType, true)
		{
			delegateTypeBase = Declaration;

			this.IsFunction = Declaration.IsFunction;

			var l = new List<BaseTypeResolutionVisitor.Wrapper>();
			if (Parameters != null)
				foreach (var p in Parameters)
					l.Add(p);
			parameters = l.ToArray();
		}

		public DelegateType(AbstractType ReturnType, FunctionLiteral Literal, IEnumerable<AbstractType> Parameters)
			: base(ReturnType, true)
		{
			delegateTypeBase = Literal;
			this.IsFunction = Literal.LiteralToken == DTokens.Function;

			var l = new List<BaseTypeResolutionVisitor.Wrapper>();
			if (Parameters != null)
				foreach (var p in Parameters)
					l.Add(p);
			parameters = l.ToArray();
		}

		public AbstractType ReturnType { get { return Base; } }

		public override AbstractType Clone(bool cloneBase)
		{
			//TODO: Clone parameters
			if (IsFunctionLiteral)
				return new DelegateType (CloneBase(cloneBase), delegateTypeBase as FunctionLiteral, Parameters);
			
			return new DelegateType(CloneBase(cloneBase), delegateTypeBase as DelegateDeclaration, Parameters);
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitDelegateType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitDelegateType(this);
		}
	}
	#endregion

	public abstract class DSymbol : DerivedDataType
	{
		protected WeakReference definition;

		public DNode Definition { get {
				return definition.Target as DNode;
			}
		}

		public bool ValidSymbol
		{
			get{ return definition.IsAlive; }
		}

		List<TemplateParameterSymbol> deducedTemplateParameters;
		public IEnumerable<TemplateParameterSymbol> DeducedTypes {
			get { return deducedTemplateParameters; }
		}
		public bool HasDeducedTypes {get{ return deducedTemplateParameters.Count != 0; }}

		public void SetDeducedTypes(IEnumerable<TemplateParameterSymbol> s)
		{
			deducedTemplateParameters = new List<TemplateParameterSymbol> ();

			if(s != null)
				foreach (var tps in s)
					if (tps != null && tps != this && tps.Base != this)
						deducedTemplateParameters.Add (tps);
		}

		public readonly int NameHash;
		public string Name {get{return Strings.TryGet (NameHash);}}

		protected DSymbol(DNode Node, AbstractType BaseType, IEnumerable<TemplateParameterSymbol> deducedTypes, bool setBaseNonStaticAccess = false)
			: base(BaseType, setBaseNonStaticAccess)
		{
			SetDeducedTypes (deducedTypes);
			
			if (Node == null)
				throw new ArgumentNullException ("Node");

			this.definition = new WeakReference(Node);
			NameHash = Node.NameHash;
		}
	}

	#region User-defined types
	public abstract class UserDefinedType : DSymbol
	{
		protected UserDefinedType(DNode Node, AbstractType baseType, IEnumerable<TemplateParameterSymbol> deducedTypes) : base(Node, baseType, deducedTypes) { }
	}

	public class AliasedType : MemberSymbol
	{
		public new DVariable Definition { get { return base.Definition as DVariable; } }

		public AliasedType(DVariable AliasDefinition, AbstractType Type, IEnumerable<TemplateParameterSymbol> deducedTypes = null)
			: base(AliasDefinition, Type, deducedTypes, false) {
		}

		public override string ToString()
		{
			return base.ToString();
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new AliasedType(Definition, CloneBase(true), DeducedTypes) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitAliasedType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitAliasedType(this);
		}
	}

	public class EnumType : UserDefinedType
	{
		public new DEnum Definition { get { return base.Definition as DEnum; } }
		public override bool NonStaticAccess
		{
			get { return true; }
			set { }
		}

		public EnumType(DEnum Enum, AbstractType BaseType) : base(Enum, BaseType, null) { }
		public EnumType(DEnum Enum) : base(Enum, new PrimitiveType(DTokens.Int, DTokens.Enum), null) { }

		public override string ToString()
		{
			return "(enum) " + base.ToString();
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new EnumType(Definition, CloneBase(cloneBase)) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitEnumType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitEnumType(this);
		}
	}

	public class StructType : TemplateIntermediateType
	{
		public StructType(DClassLike dc, IEnumerable<AbstractType> baseInterfaces, IEnumerable<TemplateParameterSymbol> deducedTypes = null) : base(dc, null, null, deducedTypes) { }

		public override string ToString()
		{
			return "(struct) " + base.ToString();
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new StructType(Definition, CloneBaseInterfaces(cloneBase), DeducedTypes) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitStructType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitStructType(this);
		}
	}

	public class UnionType : TemplateIntermediateType
	{
		public UnionType(DClassLike dc, IEnumerable<TemplateParameterSymbol> deducedTypes = null) : base(dc, null, null, deducedTypes) { }

		public override string ToString()
		{
			return "(union) " + base.ToString();
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new UnionType(Definition, DeducedTypes) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitUnionType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitUnionType(this);
		}
	}

	public class ClassType : TemplateIntermediateType
	{
		public ClassType(DClassLike dc, 
			AbstractType baseType, IEnumerable<AbstractType> baseInterfaces = null,
			IEnumerable<TemplateParameterSymbol> deducedTypes = null)
			: base(dc, baseType, baseInterfaces, deducedTypes)
		{}

		public override string ToString()
		{
			return "(class) "+base.ToString();
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new ClassType(Definition, CloneBase(cloneBase), CloneBaseInterfaces(cloneBase), DeducedTypes) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitClassType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitClassType(this);
		}
	}

	public class InterfaceType : TemplateIntermediateType
	{
		public InterfaceType(DClassLike dc, 
			IEnumerable<AbstractType> baseInterfaces=null,
			IEnumerable<TemplateParameterSymbol> deducedTypes = null) 
			: base(dc, null, baseInterfaces, deducedTypes) {}
		
		public override AbstractType Clone(bool cloneBase)
		{
			return new InterfaceType(Definition, CloneBaseInterfaces(cloneBase), DeducedTypes) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitInterfaceType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitInterfaceType(this);
		}
	}

	public class TemplateType : TemplateIntermediateType
	{
		public override bool NonStaticAccess
		{
			get
			{
				/*
				 * template t(){ void foo() { } }
				 * t!().foo must be offered for completion
				 */
				/*if(t.Base == null)
					isVariableInstance = true;
				*/
				return true;
			}
			set
			{
				
			}
		}

		public TemplateType(DClassLike dc, IEnumerable<TemplateParameterSymbol> inheritedTypeParams = null) : base(dc, null, null, inheritedTypeParams) { }

		public override AbstractType Clone(bool cloneBase)
		{
			return new TemplateType(Definition, DeducedTypes) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitTemplateType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitTemplateType(this);
		}
	}
	
	public class MixinTemplateType : TemplateType
	{
		public MixinTemplateType(DClassLike dc, IEnumerable<TemplateParameterSymbol> inheritedTypeParams = null) : base(dc, inheritedTypeParams) { }

		public override AbstractType Clone(bool cloneBase)
		{
			return new MixinTemplateType(Definition, DeducedTypes) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitMixinTemplateType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitMixinTemplateType(this);
		}
	}

	public abstract class TemplateIntermediateType : UserDefinedType
	{
		public new DClassLike Definition { get { return base.Definition as DClassLike; } }

		readonly List<BaseTypeResolutionVisitor.Wrapper> baseInterfaceWrappers = new List<BaseTypeResolutionVisitor.Wrapper>();

		bool HashCheckedBaseTypeForInterface;
		bool HasOnlyBaseInterfaces = false;
		public override AbstractType Base
		{
			get
			{
				if (HasOnlyBaseInterfaces)
					return null;

				CheckForBaseInterfaceInBaseType();

				return base.Base;
			}
		}

		void CheckForBaseInterfaceInBaseType()
		{
			if (HashCheckedBaseTypeForInterface)
				return;
			HashCheckedBaseTypeForInterface = true;

			var b = base.Base;

			if (b is InterfaceType)
			{
				baseInterfaceWrappers.Insert(0, new BaseTypeResolutionVisitor.Wrapper(b));
				HasOnlyBaseInterfaces = true;
			}
		}

		public IEnumerable<InterfaceType> BaseInterfaces
		{
			get {
				CheckForBaseInterfaceInBaseType();
				foreach (var i in baseInterfaceWrappers)
					yield return (AbstractType)i as InterfaceType;
			}
		}

		public TemplateIntermediateType(DClassLike dc, 
			AbstractType baseType, IEnumerable<AbstractType> baseInterfaces,
			IEnumerable<TemplateParameterSymbol> deducedTypes)
			: base(dc, baseType, deducedTypes)
		{
			if (baseInterfaces != null)
				foreach (var i in baseInterfaces)
					this.baseInterfaceWrappers.Add(i);
		}

		protected List<AbstractType> CloneBaseInterfaces(bool cloneBase)
		{
			var l = new List<AbstractType>();

			// See CheckForBaseInterfaceInBaseType
			bool skipFirst = HashCheckedBaseTypeForInterface && baseInterfaceWrappers.Count != 0 &&
				baseInterfaceWrappers[0].GetWithoutResolution() == Base;

			foreach (var i in baseInterfaceWrappers.Skip(skipFirst ? 1 : 0))
				l.Add(i.Clone(cloneBase));

			return l;
		}
	}

	public class EponymousTemplateType : UserDefinedType
	{
		public new EponymousTemplate Definition { get { return base.Definition as EponymousTemplate; } }

		public EponymousTemplateType(EponymousTemplate ep, IEnumerable<TemplateParameterSymbol> deducedTypes = null) : base(ep, null, deducedTypes) { }

		public override string ToString ()
		{
			return "(Eponymous Template Type) "+ Definition;
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new EponymousTemplateType(Definition, DeducedTypes);
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitEponymousTemplateType(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitEponymousTemplateType(this);
		}
	}

	public class StaticProperty : MemberSymbol
	{
		/// <summary>
		/// For keeping the weak reference up!
		/// </summary>
		DNode n;
		public readonly StaticProperties.ValueGetterHandler ValueGetter;

		public StaticProperty(DNode n, AbstractType bt, StaticProperties.ValueGetterHandler valueGetter) : base(n, bt, null)
		{
			this.n = n;
			this.ValueGetter = valueGetter;
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new StaticProperty(Definition, CloneBase(cloneBase), ValueGetter);
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitStaticProperty(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitStaticProperty(this);
		}
	}

	public class MemberSymbol : DSymbol
	{
		public MemberSymbol(DNode member, AbstractType memberType = null,
			IEnumerable<TemplateParameterSymbol> deducedTypes = null, bool setBaseNonStaticAccess = true)
			: base(member, memberType, deducedTypes, setBaseNonStaticAccess) {
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new MemberSymbol(Definition, CloneBase(cloneBase), DeducedTypes) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitMemberSymbol(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitMemberSymbol(this);
		}
	}
	
	public class TemplateParameterSymbol : MemberSymbol
	{
		public readonly TemplateParameter Parameter;
		/// <summary>
		/// Only used for template value parameters.
		/// </summary>
		public readonly ISymbolValue ParameterValue;
		public bool IsKnowinglyUndetermined;

		public TemplateParameterSymbol(TemplateParameter.Node tpn, ISemantic typeOrValue)
			: base(tpn, AbstractType.Get(typeOrValue))
		{
			IsKnowinglyUndetermined = TemplateInstanceHandler.IsNonFinalArgument(typeOrValue);
			this.Parameter = tpn.TemplateParameter;
			this.ParameterValue = typeOrValue as ISymbolValue;
		}

		public TemplateParameterSymbol(TemplateParameter tpn, ISemantic typeOrValue)
			: base(tpn != null ? tpn.Representation : null, AbstractType.Get(typeOrValue))
		{
			IsKnowinglyUndetermined = TemplateInstanceHandler.IsNonFinalArgument(typeOrValue);
			this.Parameter = tpn;
			this.ParameterValue = typeOrValue as ISymbolValue;
		}

		public override string ToString()
		{
			return "<"+(Parameter == null ? "(unknown)" : Parameter.Name)+">"+(ParameterValue!=null ? ParameterValue.ToString() : (Base ==null ? "" : Base.ToString()));
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new TemplateParameterSymbol(Parameter, ParameterValue ?? CloneBase(cloneBase) as ISemantic) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitTemplateParameterSymbol(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitTemplateParameterSymbol(this);
		}
	}
	
	/// <summary>
	/// Intermediate result when evaluating e.g. myArray[0]
	/// Required for proper completion of array access expressions (e.g. foo[0].)
	/// </summary>
	public class ArrayAccessSymbol : DerivedDataType
	{
		public readonly PostfixExpression_ArrayAccess indexExpression;

		public ArrayAccessSymbol(PostfixExpression_ArrayAccess indexExpr, AbstractType arrayValueType):
		base(arrayValueType)	{ this.indexExpression = indexExpr; }

		public override AbstractType Clone(bool cloneBase)
		{
			return new ArrayAccessSymbol(indexExpression, CloneBase(cloneBase));
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitArrayAccessSymbol(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitArrayAccessSymbol(this);
		}
	}

	public class ModuleSymbol : DSymbol
	{
		public new DModule Definition { get { return base.Definition as DModule; } }
		public override bool NonStaticAccess
		{
			get	{ return true; }
			set	{}
		}

		public ModuleSymbol(DModule mod, PackageSymbol packageBase = null) : base(mod, packageBase, null) {	}

		public override string ToString()
		{
			return "(module) "+base.ToString();
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new ModuleSymbol(Definition, CloneBase(cloneBase) as PackageSymbol) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitModuleSymbol(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitModuleSymbol(this);
		}
	}

	public class PackageSymbol : AbstractType
	{
		public readonly ModulePackage Package;

		public PackageSymbol(ModulePackage pack) {
			this.Package = pack;
		}

		public override string ToString()
		{
			return "(package) "+base.ToString();
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new PackageSymbol(Package) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitPackageSymbol(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitPackageSymbol(this);
		}
	}
	#endregion

	/// <summary>
	/// A Tuple is not a type, an expression, or a symbol. It is a sequence of any mix of types, expressions or symbols.
	/// </summary>
	public class DTuple : AbstractType
	{
		public readonly ISemantic[] Items;

		public DTuple(IEnumerable<ISemantic> items)
		{
			if (items is ISemantic[])
				Items = (ISemantic[])items;
			else if (items != null)
				Items = items.ToArray();
		}

		public bool IsExpressionTuple
		{
			get {
				return Items != null && Items.All(i => i is ISymbolValue);
			}
		}

		public bool IsTypeTuple
		{
			get
			{
				return Items != null && Items.All(i => i is AbstractType);
			}
		}

		public override AbstractType Clone(bool cloneBase)
		{
			return new DTuple(Items) { Modifier = Modifier };
		}

		public override void Accept(IResolvedTypeVisitor vis)
		{
			vis.VisitDTuple(this);
		}

		public override R Accept<R>(IResolvedTypeVisitor<R> vis)
		{
			return vis.VisitDTuple(this);
		}
	}
}
