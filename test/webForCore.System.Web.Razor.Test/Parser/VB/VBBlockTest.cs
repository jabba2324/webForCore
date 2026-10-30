// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Web.Razor.Parser;
using System.Web.Razor.Parser.SyntaxTree;
using System.Web.Razor.Resources;
using System.Web.Razor.Test.Framework;
using System.Web.Razor.Text;
using Microsoft.TestCommon;

namespace System.Web.Razor.Test.Parser.VB
{
    public class VBBlockTest : VBHtmlCodeParserTestBase
    {
        [Fact]
        public void ParseBlockMethodThrowsArgNullExceptionOnNullContext()
        {
            // Arrange
            VBCodeParser parser = new VBCodeParser();

            // Act and Assert
            Assert.Throws<InvalidOperationException>(() => parser.ParseBlock(), RazorResources.Parser_Context_Not_Set);
        }

        [Fact]
        public void ParseBlockAcceptsImplicitExpression()
        {
            ParseBlockTest("If True Then" + "\r\n"
                         + "    @foo" + "\r\n"
                         + "End If",
                new StatementBlock(
                    Factory.Code("If True Then\r\n    ").AsStatement(),
                    new ExpressionBlock(
                        Factory.CodeTransition(),
                        Factory.Code("foo")
                               .AsImplicitExpression(VBCodeParser.DefaultKeywords, acceptTrailingDot: true)
                               .Accepts(AcceptedCharacters.NonWhiteSpace)),
                    Factory.Code("\r\nEnd If")
                           .AsStatement()
                           .Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void ParseBlockAcceptsIfStatementWithinCodeBlockIfInDesignTimeMode()
        {
            ParseBlockTest("If True Then" + "\r\n"
                         + "    @If True Then" + "\r\n"
                         + "    End If" + "\r\n"
                         + "End If",
                new StatementBlock(
                    Factory.Code("If True Then\r\n    ").AsStatement(),
                    new StatementBlock(
                        Factory.CodeTransition(),
                        Factory.Code("If True Then\r\n    End If\r\n")
                               .AsStatement()
                               .Accepts(AcceptedCharacters.None)),
                    Factory.Code(@"End If")
                           .AsStatement()
                           .Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void ParseBlockSupportsSpacesInStrings()
        {
            ParseBlockTest("for each p in db.Query(\"SELECT * FROM PRODUCTS\")" + "\r\n"
                         + "    @<p>@p.Name</p>" + "\r\n"
                         + "next",
                new StatementBlock(
                    Factory.Code("for each p in db.Query(\"SELECT * FROM PRODUCTS\")\r\n")
                           .AsStatement(),
                    new MarkupBlock(
                        Factory.Markup("    "),
                        Factory.MarkupTransition(),
                        Factory.Markup("<p>"),
                        new ExpressionBlock(
                            Factory.CodeTransition(),
                            Factory.Code("p.Name")
                                   .AsImplicitExpression(VBCodeParser.DefaultKeywords)
                                   .Accepts(AcceptedCharacters.NonWhiteSpace)),
                        Factory.Markup("</p>\r\n").Accepts(AcceptedCharacters.None)),
                    Factory.Code("next")
                           .AsStatement()
                           .Accepts(AcceptedCharacters.WhiteSpace | AcceptedCharacters.NonWhiteSpace)));
        }

        [Fact]
        public void ParseBlockSupportsSimpleCodeBlock()
        {
            ParseBlockTest("Code" + "\r\n"
                         + "    If foo IsNot Nothing" + "\r\n"
                         + "        Bar(foo)" + "\r\n"
                         + "    End If" + "\r\n"
                         + "End Code",
                new StatementBlock(
                    Factory.MetaCode("Code").Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    If foo IsNot Nothing\r\n        Bar(foo)\r\n    End If\r\n")
                           .AsStatement(),
                    Factory.MetaCode("End Code").Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void ParseBlockRejectsNewlineBetweenEndAndCodeIfNotPrefixedWithUnderscore()
        {
            ParseBlockTest("Code" + "\r\n"
                         + "    If foo IsNot Nothing" + "\r\n"
                         + "        Bar(foo)" + "\r\n"
                         + "    End If" + "\r\n"
                         + "End" + "\r\n"
                         + "Code",
                new StatementBlock(
                    Factory.MetaCode("Code").Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    If foo IsNot Nothing\r\n        Bar(foo)\r\n    End If\r\nEnd\r\nCode")
                           .AsStatement()),
                new RazorError(
                    String.Format(RazorResources.ParseError_BlockNotTerminated, "Code", "End Code"),
                    SourceLocation.Zero));
        }

        [Fact]
        public void ParseBlockAcceptsNewlineBetweenEndAndCodeIfPrefixedWithUnderscore()
        {
            ParseBlockTest("Code" + "\r\n"
                         + "    If foo IsNot Nothing" + "\r\n"
                         + "        Bar(foo)" + "\r\n"
                         + "    End If" + "\r\n"
                         + "End _" + "\r\n"
                         + "_" + "\r\n"
                         + " _" + "\r\n"
                         + "Code",
                new StatementBlock(
                    Factory.MetaCode("Code").Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    If foo IsNot Nothing\r\n        Bar(foo)\r\n    End If\r\n")
                           .AsStatement(),
                    Factory.MetaCode("End _\r\n_\r\n _\r\nCode").Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void ParseBlockSupportsSimpleFunctionsBlock()
        {
            ParseBlockTest("Functions" + "\r\n"
                         + "    Public Sub Foo()" + "\r\n"
                         + "        Bar()" + "\r\n"
                         + "    End Sub" + "\r\n"
                         + "\r\n"
                         + "    Private Function Bar() As Object" + "\r\n"
                         + "        Return Nothing" + "\r\n"
                         + "    End Function" + "\r\n"
                         + "End Functions",
                new FunctionsBlock(
                    Factory.MetaCode("Functions").Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    Public Sub Foo()\r\n        Bar()\r\n    End Sub\r\n\r\n    Private Function Bar() As Object\r\n        Return Nothing\r\n    End Function\r\n")
                           .AsFunctionsBody(),
                    Factory.MetaCode("End Functions").Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void ParseBlockRejectsNewlineBetweenEndAndFunctionsIfNotPrefixedWithUnderscore()
        {
            ParseBlockTest("Functions" + "\r\n"
                         + "    If foo IsNot Nothing" + "\r\n"
                         + "        Bar(foo)" + "\r\n"
                         + "    End If" + "\r\n"
                         + "End" + "\r\n"
                         + "Functions",
                new FunctionsBlock(
                    Factory.MetaCode("Functions").Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    If foo IsNot Nothing\r\n        Bar(foo)\r\n    End If\r\nEnd\r\nFunctions")
                           .AsFunctionsBody()),
                new RazorError(
                    String.Format(RazorResources.ParseError_BlockNotTerminated, "Functions", "End Functions"),
                    SourceLocation.Zero));
        }

        [Fact]
        public void ParseBlockAcceptsNewlineBetweenEndAndFunctionsIfPrefixedWithUnderscore()
        {
            ParseBlockTest("Functions" + "\r\n"
                         + "    If foo IsNot Nothing" + "\r\n"
                         + "        Bar(foo)" + "\r\n"
                         + "    End If" + "\r\n"
                         + "End _" + "\r\n"
                         + "_" + "\r\n"
                         + " _" + "\r\n"
                         + "Functions",
                new FunctionsBlock(
                    Factory.MetaCode("Functions").Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    If foo IsNot Nothing\r\n        Bar(foo)\r\n    End If\r\n")
                           .AsFunctionsBody(),
                    Factory.MetaCode("End _\r\n_\r\n _\r\nFunctions").Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void ParseBlockCorrectlyHandlesExtraEndsInEndCode()
        {
            ParseBlockTest("Code" + "\r\n"
                         + "    Bar End" + "\r\n"
                         + "End Code",
                new StatementBlock(
                    Factory.MetaCode("Code").Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    Bar End\r\n").AsStatement(),
                    Factory.MetaCode("End Code").Accepts(AcceptedCharacters.None)));
        }

        [Fact]
        public void ParseBlockCorrectlyHandlesExtraEndsInEndFunctions()
        {
            ParseBlockTest("Functions" + "\r\n"
                         + "    Bar End" + "\r\n"
                         + "End Functions",
                new FunctionsBlock(
                    Factory.MetaCode("Functions").Accepts(AcceptedCharacters.None),
                    Factory.Code("\r\n    Bar End\r\n").AsFunctionsBody().AutoCompleteWith(null, atEndOfSpan: false),
                    Factory.MetaCode("End Functions").Accepts(AcceptedCharacters.None)));
        }

        [Theory]
        [InlineData("If", "End", "If")]
        [InlineData("Try", "End", "Try")]
        [InlineData("While", "End", "While")]
        [InlineData("Using", "End", "Using")]
        [InlineData("With", "End", "With")]
        public void KeywordAllowsNewlinesIfPrefixedByUnderscore(string startKeyword, string endKeyword1, string endKeyword2)
        {
            string code = startKeyword + "\r\n"
                        + "    ' In the block" + "\r\n"
                        + endKeyword1 + " _" + "\r\n"
                        + "_" + "\r\n"
                        + "_" + "\r\n"
                        + "_" + "\r\n"
                        + "_" + "\r\n"
                        + "_" + "\r\n"
                        + "  " + endKeyword2 + "\r\n";
            ParseBlockTest(code + "foo bar baz",
                new StatementBlock(
                    Factory.Code(code)
                           .AsStatement()
                           .Accepts(AcceptedCharacters.None)));
        }

        [Theory]
        [InlineData("While", "EndWhile", "End While")]
        [InlineData("If", "EndIf", "End If")]
        [InlineData("Select", "EndSelect", "End Select")]
        [InlineData("Try", "EndTry", "End Try")]
        [InlineData("With", "EndWith", "End With")]
        [InlineData("Using", "EndUsing", "End Using")]
        public void EndTerminatedKeywordRequiresSpaceBetweenEndAndKeyword(string startKeyword, string wrongEndKeyword, string endKeyword)
        {
            string code = startKeyword + "\r\n"
                        + "    ' This should not end the code" + "\r\n"
                        + "    " + wrongEndKeyword + "\r\n"
                        + "    ' But this should" + "\r\n"
                        + endKeyword;
            ParseBlockTest(code,
                new StatementBlock(
                    Factory.Code(code)
                           .AsStatement()
                           .Accepts(AcceptedCharacters.None)));
        }

        [Theory]
        [InlineData("While", "End While", false)]
        [InlineData("Do", "Loop", true)]
        [InlineData("If", "End If", false)]
        [InlineData("Select", "End Select", false)]
        [InlineData("For", "Next", true)]
        [InlineData("Try", "End Try", false)]
        [InlineData("With", "End With", false)]
        [InlineData("Using", "End Using", false)]
        public void EndSequenceInString(string keyword, string endSequence, bool acceptToEndOfLine)
        {
            string code = keyword + "\r\n"
                        + "    \"" + endSequence + "\"" + "\r\n"
                        + endSequence + (acceptToEndOfLine ? " foo bar baz" : "") + "\r\n";
            ParseBlockTest(code + "biz boz",
                new StatementBlock(
                    Factory.Code(code).AsStatement().Accepts(GetAcceptedCharacters(acceptToEndOfLine))));
        }

        [Theory]
        [InlineData("While", "End While", false)]
        [InlineData("Do", "Loop", true)]
        [InlineData("If", "End If", false)]
        [InlineData("Select", "End Select", false)]
        [InlineData("For", "Next", true)]
        [InlineData("Try", "End Try", false)]
        [InlineData("With", "End With", false)]
        [InlineData("Using", "End Using", false)]
        private void CommentedEndSequence(string keyword, string endSequence, bool acceptToEndOfLine)
        {
            string code = keyword + "\r\n"
                        + "    '" + endSequence + "\r\n"
                        + endSequence + (acceptToEndOfLine ? @" foo bar baz" : "") + "\r\n";
            ParseBlockTest(code + "biz boz",
                new StatementBlock(
                    Factory.Code(code).AsStatement().Accepts(GetAcceptedCharacters(acceptToEndOfLine))));
        }

        [Theory]
        [InlineData("While", "End While", false)]
        [InlineData("Do", "Loop", true)]
        [InlineData("If", "End If", false)]
        [InlineData("Select", "End Select", false)]
        [InlineData("For", "Next", true)]
        [InlineData("Try", "End Try", false)]
        [InlineData("With", "End With", false)]
        [InlineData("SyncLock", "End SyncLock", false)]
        [InlineData("Using", "End Using", false)]
        private void NestedKeywordBlock(string keyword, string endSequence, bool acceptToEndOfLine)
        {
            string code = keyword + "\r\n"
                        + "    " + keyword + "\r\n"
                        + "        Bar(foo)" + "\r\n"
                        + "    " + endSequence + "\r\n"
                        + endSequence + (acceptToEndOfLine ? " foo bar baz" : "") + "\r\n";
            ParseBlockTest(code + "biz boz",
                new StatementBlock(
                    Factory.Code(code).AsStatement().Accepts(GetAcceptedCharacters(acceptToEndOfLine))));
        }

        [Theory]
        [InlineData("While True", "End While", false)]
        [InlineData("Do", "Loop", true)]
        [InlineData("If foo IsNot Nothing", "End If", false)]
        [InlineData("Select Case foo", "End Select", false)]
        [InlineData("For Each p in Products", "Next", true)]
        [InlineData("Try", "End Try", false)]
        [InlineData("With", "End With", false)]
        [InlineData("SyncLock", "End SyncLock", false)]
        [InlineData("Using", "End Using", false)]
        private void SimpleKeywordBlock(string keyword, string endSequence, bool acceptToEndOfLine)
        {
            string code = keyword + "\r\n"
                        + "    Bar(foo)" + "\r\n"
                        + endSequence + (acceptToEndOfLine ? " foo bar baz" : "") + "\r\n";
            ParseBlockTest(code + "biz boz",
                new StatementBlock(
                    Factory.Code(code).AsStatement().Accepts(GetAcceptedCharacters(acceptToEndOfLine))));
        }

        [Theory]
        [InlineData("While True", "Exit While", "End While", false)]
        [InlineData("Do", "Exit Do", "Loop", true)]
        [InlineData("For Each p in Products", "Exit For", "Next", true)]
        [InlineData("While True", "Continue While", "End While", false)]
        [InlineData("Do", "Continue Do", "Loop", true)]
        [InlineData("For Each p in Products", "Continue For", "Next", true)]
        private void KeywordWithExitOrContinue(string startKeyword, string exitKeyword, string endKeyword, bool acceptToEndOfLine)
        {
            string code = startKeyword + "\r\n"
                         + "    ' This is before the exit" + "\r\n"
                         + "    " + exitKeyword + "\r\n"
                         + "    ' This is after the exit" + "\r\n"
                         + endKeyword + "\r\n";
            ParseBlockTest(code + "foo bar baz",
                new StatementBlock(
                    Factory.Code(code).AsStatement().Accepts(GetAcceptedCharacters(acceptToEndOfLine))));
        }

        private AcceptedCharacters GetAcceptedCharacters(bool acceptToEndOfLine)
        {
            return acceptToEndOfLine ?
                AcceptedCharacters.WhiteSpace | AcceptedCharacters.NonWhiteSpace :
                AcceptedCharacters.None;
        }
    }
}
