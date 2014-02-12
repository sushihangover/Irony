/* **********************************************************************************
 * Copyright (c) Robert Nees (https://github.com/sushihangover/Irony)
 * This source code is subject to terms and conditions of the MIT License
 * for Irony. A copy of the license can be found in the License.txt file
 * at the root of this distribution.
 * By using this source code in any fashion, you are agreeing to be bound by the terms of the
 * MIT License.
 * You must not remove this notice from this software.
 * **********************************************************************************/
using System;
using System.Linq;
using System.Resources;
using System.Reflection;
using System.Collections.Generic;
using Mono.TextEditor.Highlighting;
using Mono.TextEditor;
using System.Xml;
using Irony.GrammarExplorer;
using Gtk;

namespace Irony.GrammarExplorer
{
	public class IronyParserStatesSyntaxMode : SyntaxMode
	{
		public IronyParserStatesSyntaxMode (TextDocument doc) : base (doc)
		{
			ResourceStreamProvider provider = new ResourceStreamProvider (Assembly.GetExecutingAssembly(), "Irony.ParserStates");
			using (var stream = provider.Open ()) {
				try {
					SyntaxMode baseMode = SyntaxMode.Read (stream);
					this.rules = new List<Rule> (baseMode.Rules);
					this.keywords = new List<Keywords> (baseMode.Keywords);
					this.spans = new List<Span> (baseMode.Spans).ToArray ();
					this.matches = baseMode.Matches;
					this.prevMarker = baseMode.PrevMarker;
					this.SemanticRules = new List<SemanticRule> (baseMode.SemanticRules);
					this.keywordTable = baseMode.keywordTable;
					this.keywordTableIgnoreCase = baseMode.keywordTableIgnoreCase;
					this.properties = baseMode.Properties;
				} catch {
					// Resource does not exist or is corrupt
					dlgShowException showExceptionDialog = new dlgShowException ("Irony Parser States Syntax Resource does not exist or is corrupt");
					showExceptionDialog.Response += (object o, ResponseArgs args) => showExceptionDialog.Destroy ();
				}
			}
		}

		public override SpanParser CreateSpanParser (DocumentLine line, CloneableStack<Span> spanStack)
		{
			return new IronySpanParser (this, spanStack ?? line.StartSpan.Clone ());
		}
		
		class IronyBlockSpan : Span
		{
			public int Offset {
				get;
				set;
			}
			
			public IronyBlockSpan (int offset)
			{
				this.Offset = offset;
				this.Rule = "mode:text/x-csharp";
				this.Begin = new Regex ("}");
				this.End = new Regex ("}");
				this.TagColor = "Keyword(Access)";
			}
			
			public override string ToString ()
			{
				return string.Format ("[IronyBlockSpan: Offset={0}]", Offset);
			}
		}
		
		class ForcedIronyBlockSpan : Span
		{
			public ForcedIronyBlockSpan ()
			{
				this.Rule = "mode:text/x-csharp";
				this.Begin = new Regex ("%{");
				this.End = new Regex ("%}");
				this.TagColor = "Keyword(Access)";
			}
		}
		
		class IronyDefinitionSpan : Span
		{
			public IronyDefinitionSpan ()
			{
				this.Rule = "token";
				this.Begin = this.End = new Regex ("%%");
				this.TagColor = "Keyword(Access)";
			}
		}
		
		protected class IronySpanParser : SpanParser
		{
			public IronySpanParser (SyntaxMode mode, CloneableStack<Span> spanStack) : base (mode, spanStack)
			{
			}
			
			protected override bool ScanSpan (ref int i)
			{
				bool hasIronyDefinitonSpan = spanStack.Any (s => s is IronyDefinitionSpan);
				int textOffset = i - StartOffset;
				
				if (textOffset + 1 < CurText.Length && CurText[textOffset] == '%')  {
					char next = CurText[textOffset + 1];
					if (next == '{') {
						FoundSpanBegin (new ForcedIronyBlockSpan (), i, 2);
						i++;
						return true;
					}
					
					if (!hasIronyDefinitonSpan && next == '%') {
						FoundSpanBegin (new IronyDefinitionSpan (), i, 2);
						return true;
					}
					
					if (next == '}' && spanStack.Any (s => s is ForcedIronyBlockSpan)) {
						foreach (Span span in spanStack.Clone ()) {
							FoundSpanEnd (span, i, span.End.Pattern.Length);
							if (span is ForcedIronyBlockSpan)
								break;
						}
						return false;
					}
				}
				
				
				if (CurSpan is IronyDefinitionSpan && CurText[textOffset] == '{' && hasIronyDefinitonSpan && !spanStack.Any (s => s is IronyBlockSpan)) {
					FoundSpanBegin (new IronyBlockSpan (i), i, 1);
					return true;
				}
				
				return base.ScanSpan (ref i);
			}
			
			protected override bool ScanSpanEnd (Mono.TextEditor.Highlighting.Span cur, ref int i)
			{
				IronyBlockSpan jbs = cur as IronyBlockSpan;
				int textOffset = i - StartOffset;
				if (jbs != null) {
					if (CurText[textOffset] == '}') {
						int brackets = 0;
						bool isInString = false, isInChar = false, isVerbatimString = false;
						bool isInLineComment  = false, isInBlockComment = false;
						
						for (int j = jbs.Offset; j <= i; j++) {
							char ch = doc.GetCharAt (j);
							switch (ch) {
								case '\n':
								case '\r':
									isInLineComment = false;
									if (!isVerbatimString)
										isInString = false;
									break;
								case '/':
									if (isInBlockComment) {
										if (j > 0 && doc.GetCharAt (j - 1) == '*') 
											isInBlockComment = false;
									} else if (!isInString && !isInChar && j + 1 < doc.TextLength) {
										char nextChar = doc.GetCharAt (j + 1);
										if (nextChar == '/')
											isInLineComment = true;
										if (!isInLineComment && nextChar == '*')
											isInBlockComment = true;
									}
									break;
								case '\\':
									if (isInChar || (isInString && !isVerbatimString))
										j++;
									break;
								case '@':
									if (!(isInString || isInChar || isInLineComment || isInBlockComment) && j + 1 < doc.TextLength && doc.GetCharAt (j + 1) == '"') {
										isInString = true;
										isVerbatimString = true;
										j++;
									}
									break;
								case '"':
									if (!(isInChar || isInLineComment || isInBlockComment))  {
										if (isInString && isVerbatimString && j + 1 < doc.TextLength && doc.GetCharAt (j + 1) == '"') {
											j++;
										} else {
											isInString = !isInString;
											isVerbatimString = false;
										}
									}
									break;
								case '\'':
									if (!(isInString || isInLineComment || isInBlockComment)) 
										isInChar = !isInChar;
									break;
								case '{':
									if (!(isInString || isInChar || isInLineComment || isInBlockComment))
										brackets++;
									break;
								case '}':
									if (!(isInString || isInChar || isInLineComment || isInBlockComment))
										brackets--;
									break;
							}
						}
						if (brackets == 0) {
							FoundSpanEnd (cur, i, 1);
							return true;
						}
						return false;
					}
				}
				
				if (cur is ForcedIronyBlockSpan) {
					if (textOffset + 1 < CurText.Length && CurText[textOffset] == '%' && CurText[textOffset + 1] == '}') {
						FoundSpanEnd (cur, i, 2);
						return true;
					}
				}
				
				if (cur is IronyDefinitionSpan) {
					if (textOffset + 1 < CurText.Length && CurText[textOffset] == '%' && CurText[textOffset + 1] == '%') {
						FoundSpanEnd (cur, i, 2);
						return true;
					}
				}
				return base.ScanSpanEnd (cur, ref i);
			}
		}
	}
}

