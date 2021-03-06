﻿using System;
using D_Parser.Parser;

namespace D_Parser.Dom.Expressions
{
	public interface IntermediateIdType : ISyntaxRegion
	{
		bool ModuleScoped { get; set; }
		int IdHash{get;}
	}

	/// <summary>
	/// Identifier as well as literal primary expression
	/// </summary>
	public class IdentifierExpression : PrimaryExpression, IntermediateIdType
	{
		public bool ModuleScoped {
			get;
			set;
		}

		public int IdHash {
			get { return ValueStringHash; }
		}

		public bool IsIdentifier { get { return ValueStringHash != 0 && Format == LiteralFormat.None; } }

		public readonly object Value;
		public readonly int ValueStringHash;
		public readonly int EscapeStringHash;

		public string StringValue { get { return ValueStringHash != 0 ? Strings.TryGet(ValueStringHash) : Value as string; } }

		public readonly LiteralFormat Format;
		public readonly LiteralSubformat Subformat;
		//public IdentifierExpression() { }
		public IdentifierExpression(object Val)
		{
			Value = Val;
			Format = LiteralFormat.None;
		}

		public IdentifierExpression(object Val, LiteralFormat LiteralFormat, LiteralSubformat Subformat = 0, string escapeString = null)
		{
			Value = Val;
			this.Format = LiteralFormat;
			this.Subformat = Subformat;
			if (escapeString != null)
			{
				Strings.Add(escapeString);
				EscapeStringHash = escapeString.GetHashCode();
			}
		}

		public IdentifierExpression(string Value, LiteralFormat LiteralFormat = LiteralFormat.None, LiteralSubformat Subformat = 0)
		{ 
			Strings.Add(Value);
			ValueStringHash = Value.GetHashCode(); 
			this.Format = LiteralFormat; 
			this.Subformat = Subformat;
		}

		public override string ToString()
		{
			if (Format != Parser.LiteralFormat.None)
				switch (Format)
			{
				case Parser.LiteralFormat.CharLiteral:
					return "'" + (EscapeStringHash != 0 ? ("\\"+Strings.TryGet(EscapeStringHash)) : Value ?? "") + "'";
				case Parser.LiteralFormat.StringLiteral:
					return "\"" + StringValue + "\"";
				case Parser.LiteralFormat.VerbatimStringLiteral:
					return "r\"" + StringValue + "\"";
				}
			else if (IsIdentifier)
			{
				return (ModuleScoped ? "." : "") + StringValue;
			}

			if (Value is decimal)
				return ((decimal)Value).ToString(System.Globalization.CultureInfo.InvariantCulture);

			return Value == null ? null : Value.ToString();
		}

		public CodeLocation Location
		{
			get;
			set;
		}

		public CodeLocation EndLocation
		{
			get;
			set;
		}

		public void Accept(ExpressionVisitor vis)
		{
			vis.Visit(this);
		}

		public R Accept<R>(ExpressionVisitor<R> vis)
		{
			return vis.Visit(this);
		}
	}
}

