// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Web.Razor.Generator;
using System.Web.Razor.Parser;
using System.Web.Razor.Parser.SyntaxTree;
using System.Web.Razor.Test.Framework;
using System.Web.Razor.Text;
using Microsoft.TestCommon;

namespace System.Web.Razor.Test.Parser.VB
{
    public class VBDirectiveTest : VBHtmlCodeParserTestBase
    {
        [Fact]
        public void VB_Code_Directive()
        {
            ParseBlockTest("@Code" + "\r\n"
                         + "    foo()" + "\r\n"
                         + "End Code" + "\r\n"
                         + "' Not part of the block",
                new StatementBlock(
                    Factory.CodeTransition(SyntaxConstants.TransitionString)
                           .Accepts(AcceptedCharacters.None),
                    Factory.MetaCode("Code")
                           .Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    foo()\r\n")
                           .AsStatement()
                           .With(new AutoCompleteEditHandler(VBLanguageCharacteristics.Instance.TokenizeString)),
                    Factory.MetaCode("End Code")
                           .Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void VB_Functions_Directive()
        {
            ParseBlockTest("@Functions" + "\r\n"
                         + "    Public Function Foo() As String" + "\r\n"
                         + "        Return \"Foo\"" + "\r\n"
                         + "    End Function" + "\r\n"
                         + "\r\n"
                         + "    Public Sub Bar()" + "\r\n"
                         + "    End Sub" + "\r\n"
                         + "End Functions" + "\r\n"
                         + "' Not part of the block",
                new FunctionsBlock(
                    Factory.CodeTransition(SyntaxConstants.TransitionString)
                           .Accepts(AcceptedCharacters.None),
                    Factory.MetaCode("Functions")
                           .Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    Public Function Foo() As String\r\n        Return \"Foo\"\r\n    End Function\r\n\r\n    Public Sub Bar()\r\n    End Sub\r\n")
                           .AsFunctionsBody(),
                    Factory.MetaCode("End Functions")
                           .Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void VB_Section_Directive()
        {
            ParseBlockTest("@Section Header" + "\r\n"
                         + "    <p>Foo</p>" + "\r\n"
                         + "End Section",
                new SectionBlock(new SectionCodeGenerator("Header"),
                    Factory.CodeTransition(SyntaxConstants.TransitionString),
                    Factory.MetaCode("Section Header"),
                    new MarkupBlock(
                        Factory.Markup("\r\n    <p>Foo</p>\r\n")),
                    Factory.MetaCode("End Section")
                           .Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void SessionStateDirectiveWorks()
        {
            ParseBlockTest("@SessionState InProc" + "\r\n",
                new DirectiveBlock(
                    Factory.CodeTransition(),
                    Factory.MetaCode("SessionState ")
                        .Accepts(AcceptedCharacters.None),
                    Factory.MetaCode("InProc\r\n")
                        .Accepts(AcceptedCharacters.None)
                        .With(new RazorDirectiveAttributeCodeGenerator("SessionState", "InProc"))
                )
            );
        }

        [Fact]
        public void SessionStateDirectiveIsCaseInsensitive()
        {
            ParseBlockTest("@sessionstate disabled" + "\r\n",
                new DirectiveBlock(
                    Factory.CodeTransition(),
                    Factory.MetaCode("sessionstate ")
                        .Accepts(AcceptedCharacters.None),
                    Factory.MetaCode("disabled\r\n")
                        .Accepts(AcceptedCharacters.None)
                        .With(new RazorDirectiveAttributeCodeGenerator("SessionState", "disabled"))
                )
            );
        }

        [Fact]
        public void VB_Helper_Directive()
        {
            ParseBlockTest("@Helper Strong(s as String)" + "\r\n"
                         + "    s = s.ToUpperCase()" + "\r\n"
                         + "    @<strong>s</strong>" + "\r\n"
                         + "End Helper",
                new HelperBlock(new HelperCodeGenerator(new LocationTagged<string>("Strong(s as String)", 8, 0, 8), headerComplete: true),
                    Factory.CodeTransition(SyntaxConstants.TransitionString),
                    Factory.MetaCode("Helper ")
                           .Accepts(AcceptedCharacters.None),
                    Factory.Code("Strong(s as String)").Hidden(),
                    new StatementBlock(
                        Factory.Code("\r\n    s = s.ToUpperCase()\r\n")
                               .AsStatement(),
                        new MarkupBlock(
                            Factory.Markup("    "),
                            Factory.MarkupTransition(SyntaxConstants.TransitionString),
                            Factory.Markup("<strong>s</strong>\r\n")
                                   .Accepts(AcceptedCharacters.None)),
                        Factory.EmptyVB()
                               .AsStatement(),
                        Factory.MetaCode("End Helper")
                               .Accepts(AcceptedCharacters.None))));
        }
    }
}
