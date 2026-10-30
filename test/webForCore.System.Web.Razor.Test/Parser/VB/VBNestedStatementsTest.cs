// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Web.Razor.Parser.SyntaxTree;
using System.Web.Razor.Test.Framework;
using System.Web.Razor.Tokenizer.Symbols;
using Microsoft.TestCommon;

namespace System.Web.Razor.Test.Parser.VB
{
    public class VBNestedStatementsTest : VBHtmlCodeParserTestBase
    {
        [Fact]
        public void VB_Nested_If_Statement()
        {
            ParseBlockTest("@If True Then" + "\r\n"
                         + "    If False Then" + "\r\n"
                         + "    End If" + "\r\n"
                         + "End If",
                new StatementBlock(
                    Factory.CodeTransition(),
                    Factory.Code("If True Then\r\n    If False Then\r\n    End If\r\nEnd If")
                           .AsStatement()
                           .Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void VB_Nested_Do_Statement()
        {
            ParseBlockTest("@Do While True" + "\r\n"
                         + "    Do" + "\r\n"
                         + "    Loop Until False" + "\r\n"
                         + "Loop",
                new StatementBlock(
                    Factory.CodeTransition(),
                    Factory.Code("Do While True\r\n    Do\r\n    Loop Until False\r\nLoop")
                           .AsStatement()
                           .Accepts(AcceptedCharacters.AnyExceptNewline)));
        }

        [Fact]
        public void VB_Nested_Markup_Statement_In_If()
        {
            ParseBlockTest("@If True Then" + "\r\n"
                         + "    @<p>Tag</p>" + "\r\n"
                         + "End If",
                new StatementBlock(
                    Factory.CodeTransition(),
                    Factory.Code("If True Then\r\n")
                           .AsStatement(),
                    new MarkupBlock(
                        Factory.Markup("    "),
                        Factory.MarkupTransition(),
                        Factory.Markup("<p>Tag</p>\r\n")
                               .Accepts(AcceptedCharacters.None)),
                    Factory.Code("End If")
                           .AsStatement()
                           .Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void VB_Nested_Markup_Statement_In_Code()
        {
            ParseBlockTest("@Code" + "\r\n"
                         + "    Foo()" + "\r\n"
                         + "    @<p>Tag</p>" + "\r\n"
                         + "    Bar()" + "\r\n"
                         + "End Code",
                new StatementBlock(
                    Factory.CodeTransition(),
                    Factory.MetaCode("Code")
                           .Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    Foo()\r\n")
                           .AsStatement(),
                    new MarkupBlock(
                        Factory.Markup("    "),
                        Factory.MarkupTransition(),
                        Factory.Markup("<p>Tag</p>\r\n")
                               .Accepts(AcceptedCharacters.None)),
                    Factory.Code("    Bar()\r\n")
                           .AsStatement(),
                    Factory.MetaCode("End Code")
                           .Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void VB_Nested_Markup_Statement_In_Do()
        {
            ParseBlockTest("@Do" + "\r\n"
                         + "    @<p>Tag</p>" + "\r\n"
                         + "Loop While True",
                new StatementBlock(
                    Factory.CodeTransition(),
                    Factory.Code("Do\r\n")
                           .AsStatement(),
                    new MarkupBlock(
                        Factory.Markup("    "),
                        Factory.MarkupTransition(),
                        Factory.Markup("<p>Tag</p>\r\n")
                               .Accepts(AcceptedCharacters.None)),
                    Factory.Code("Loop While True")
                           .AsStatement()
                           .Accepts(AcceptedCharacters.AnyExceptNewline)));
        }

        [Fact]
        public void VB_Nested_Single_Line_Markup_Statement_In_Do()
        {
            ParseBlockTest("@Do" + "\r\n"
                         + "    @:<p>Tag" + "\r\n"
                         + "Loop While True",
                new StatementBlock(
                    Factory.CodeTransition(),
                    Factory.Code("Do\r\n")
                           .AsStatement(),
                    new MarkupBlock(
                        Factory.Markup("    "),
                        Factory.MarkupTransition(),
                        Factory.MetaMarkup(":", HtmlSymbolType.Colon),
                        Factory.Markup("<p>Tag\r\n")
                               .Accepts(AcceptedCharacters.None)),
                    Factory.Code("Loop While True")
                           .AsStatement()
                           .Accepts(AcceptedCharacters.AnyExceptNewline)));
        }

        [Fact]
        public void VB_Nested_Implicit_Expression_In_If()
        {
            ParseBlockTest("@If True Then" + "\r\n"
                         + "    @Foo.Bar" + "\r\n"
                         + "End If",
                new StatementBlock(
                    Factory.CodeTransition(),
                    Factory.Code("If True Then\r\n    ")
                           .AsStatement(),
                    new ExpressionBlock(
                        Factory.CodeTransition(),
                        Factory.Code("Foo.Bar")
                               .AsExpression()
                               .Accepts(AcceptedCharacters.NonWhiteSpace)),
                    Factory.Code("\r\nEnd If")
                           .AsStatement()
                           .Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void VB_Nested_Explicit_Expression_In_If()
        {
            ParseBlockTest("@If True Then" + "\r\n"
                         + "    @(Foo.Bar + 42)" + "\r\n"
                         + "End If",
                new StatementBlock(
                    Factory.CodeTransition(),
                    Factory.Code("If True Then\r\n    ")
                           .AsStatement(),
                    new ExpressionBlock(
                        Factory.CodeTransition(),
                        Factory.MetaCode("(")
                               .Accepts(AcceptedCharacters.None),
                        Factory.Code("Foo.Bar + 42")
                               .AsExpression(),
                        Factory.MetaCode(")")
                               .Accepts(AcceptedCharacters.None)),
                    Factory.Code("\r\nEnd If")
                           .AsStatement()
                           .Accepts(AcceptedCharacters.None)));
        }
    }
}
