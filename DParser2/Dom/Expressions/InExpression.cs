﻿using System;
using D_Parser.Parser;

namespace D_Parser.Dom.Expressions
{
	/// <summary>
	/// a in b; a !in b
	/// </summary>
	public class InExpression : OperatorBasedExpression
	{
		public bool Not;

		public InExpression(bool notIn)
		{
			Not = notIn;
			OperatorToken = DTokens.In;
		}

		public override string ToString()
		{
			return LeftOperand.ToString() + (Not ? " !" : " ") + "in " + RightOperand.ToString();
		}

		public override void Accept(ExpressionVisitor vis)
		{
			vis.Visit(this);
		}

		public override R Accept<R>(ExpressionVisitor<R> vis)
		{
			return vis.Visit(this);
		}
	}
}

