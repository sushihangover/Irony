using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using Mono.TextEditor;

namespace Irony.GrammarExplorer
{
//	public abstract class SemanticRule
//	{
//		public abstract void Analyze (TextDocument doc, DocumentLine line, Chunk startChunk, int startOffset, int endOffset);
//	}
	
	public class HighlightIronySemanticRule : SemanticRule
	{
		const string urlRegexStr = @"sushi";
		
		public static readonly Regex UrlRegex  = new Regex (urlRegexStr, RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		public static readonly Regex MailRegex = new Regex (@"[\w\d._%+-]+@[\w\d.-]+\.\w+", RegexOptions.Compiled);
		string syntax;
		
		public HighlightIronySemanticRule (string syntax)
		{
			this.syntax = syntax;
		}
		
		bool inUpdate = false;
		public override void Analyze (TextDocument doc, DocumentLine line, Chunk startChunk, int startOffset, int endOffset)
		{
			if (endOffset <= startOffset || startOffset >= doc.TextLength || inUpdate)
				return;
			inUpdate = true;
			try {
				string text = doc.GetTextAt (startOffset, System.Math.Min (endOffset, doc.TextLength) - startOffset);
				int startColumn = startOffset - line.Offset;
				var markers = new List <UrlMarker> (line.Markers.Where (m => m is UrlMarker).Cast<UrlMarker> ());
				markers.ForEach (m => doc.RemoveMarker (m, false));
				foreach (System.Text.RegularExpressions.Match m in UrlRegex.Matches (text)) {
					doc.AddMarker (line, new UrlMarker (doc, line, m.Value, UrlType.Url, syntax, startColumn + m.Index, startColumn + m.Index + m.Length), false);
				}
				foreach (System.Text.RegularExpressions.Match m in MailRegex.Matches (text)) {
					doc.AddMarker (line, new UrlMarker (doc, line, m.Value, UrlType.Email, syntax, startColumn + m.Index, startColumn + m.Index + m.Length), false);
				}
			} finally {
				inUpdate = false;
			}
		}
		
	}
}
