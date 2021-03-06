﻿using System;
using D_Parser.Parser;

namespace D_Parser.Dom.Expressions
{
	/// <summary>
	/// a + b; a - b;
	/// </summary>
	public class AddExpression : OperatorBasedExpression
	{
		public AddExpression(bool isMinus)
		{
			OperatorToken = isMinus ? DTokens.Minus : DTokens.Plus;
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

