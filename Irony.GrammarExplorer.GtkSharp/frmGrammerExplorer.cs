#region License
/* **********************************************************************************
 * Copyright (c) Robert Nees (https://github.com/sushihangover/Irony)
 * This source code is subject to terms and conditions of the MIT License
 * for Irony. A copy of the license can be found in the License.txt file
 * at the root of this distribution.
 * By using this source code in any fashion, you are agreeing to be bound by the terms of the
 * MIT License.
 * You must not remove this notice from this software.
 * **********************************************************************************/
//Original Windows.Form Version by Roman Ivantsov
//with Windows.Form contributions by Andrew Bradnan and Alexey Yakovlev
#endregion
using Gtk;
using Gdk;
using GLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Xml;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.TextEditor;
using Mono.TextEditor.Highlighting;
using IgeMacIntegration;
using Irony.Ast;
using Irony.Parsing;
using Irony.GrammerExplorer;

namespace Irony.GrammarExplorer
{
	using ScriptException = Irony.Interpreter.ScriptException;

	// Settings conflict with Gtk.Settings
	using MyApp = Irony.GrammarExplorer.Properties;

	//that's the only place we use stuff from Irony.Interpreter
	public partial class MainWindow: Gtk.Window
	{
		public MainWindow () : base (Gtk.WindowType.Toplevel)
		{
			_teEditor = new TextEditor ();
			_txtTerms = new TextEditor ();
			_txtNonTerms = new TextEditor ();
			_txtParserStates = new TextEditor ();

			Build ();
			_grammarLoader.AssemblyUpdated += GrammarAssemblyUpdated;

			// Setup GTK Models(listStore) as no way to do this in MonoDevelop/Stetic, so dumb...yuk....
			SetupModel_gridGrammarErrors ();
			SetupModel_gridCompileErrors ();
			SetupModel_cboGrammars ();
			SetModel_gridParserTrace ();
			SetModel_lstTokens ();
			SetupModel_btnManageGrammars ();
			SetupModel_tvParseTree ();
			SetupModel_tvAST ();
			SetOSX_Menus ();

			try {
				swEditor.Child = _teEditor;
				TextEditorOptions foo = new TextEditorOptions ();
				_teEditor.Options = foo;
				_teEditor.Options.ColorScheme = "TangoLight";
				_teEditor.Options.ShowIconMargin = false;
				_teEditor.Options.ShowFoldMargin = false;
				_teEditor.Options.ShowRuler = false;
				_teEditor.Options = foo;
				_teEditor.Options.WrapLines = true;

				_teEditor.SelectionChanged += (object sender, EventArgs e) => OnBtnLocateClicked(sender, e);

				swEditor.ShowAll ();
				sWinTerminals.Child = _txtTerms;
				sWinTerminals.ShowAll ();
				_txtTerms.Document.ReadOnly = true;
				// ColorScheme of the TextEditor.Options = "Default, TangoLight, Visual Studio, IrBlack, GEdit, Brown, C64, Oblivion"
				_txtTerms.Options.ColorScheme = "TangoLight";
				_txtTerms.Options.ShowIconMargin = false;
				_txtTerms.Options.ShowFoldMargin = false;
				_txtTerms.Options.ShowRuler = false;

				sWinNonTerminals.Child = _txtNonTerms;
				sWinNonTerminals.ShowAll ();
				_txtNonTerms.Document.ReadOnly = true;

				sWinParserStates.Child = _txtParserStates;
				sWinParserStates.ShowAll ();
				_txtParserStates.Document.ReadOnly = true;

			} catch (Exception error) {
				dlgShowException showExceptionDialog = new dlgShowException (error.Message);
				showExceptionDialog.Response += (object o, ResponseArgs args) => showExceptionDialog.Destroy ();
				Application.Quit ();
			}

			tabGrammar.CurrentPage = 0;
			tabBottom.CurrentPage = 0;
			fmExploreGrammarWindowLoad ();
			this.Present ();
		}

		private void SetOSX_Menus ()
		{
			//			if (PlatformDetection.IsMac) {
			if (OpenTK.Configuration.RunningOnMacOS) {
				mbExplorer.Hide ();

				IgeMacMenu.GlobalKeyHandlerEnabled = true;
				IgeMacMenu.MenuBar = mbExplorer;
				// TOOD Fix the quit action via menu to call proper event so user.config is updated 
//				IgeMacMenu.QuitMenuItem = QuitAction;
			}
		}

		protected void OnDeleteEvent (object sender, DeleteEventArgs a)
		{
			fmExploreGrammarWindowClosing ();
			Application.Quit ();
			a.RetVal = true;
		}

		IronySyntaxMode _IronySyntaxMode;
		TextEditor _teEditor;
		TextEditor _txtTerms;
		TextEditor _txtNonTerms;
		TextEditor _txtParserStates;
		System.Text.RegularExpressions.Regex _regexCleanWhiteSpace = new System.Text.RegularExpressions.Regex (@"[ ]{2,}", RegexOptions.None);
		bool _fullScreen;
		Grammar _grammar;
		LanguageData _language;
		Parser _parser;
		ParseTree _parseTree;
		ScriptException _runtimeError;
		GrammarLoader _grammarLoader = new GrammarLoader ();
		bool _loaded;

		private void SetupModel_tvAST ()
		{
			tvAST.AppendColumn ("AST Tree", new Gtk.CellRendererText (), "text", 0);
			TreeStore modelAstTree = new TreeStore (typeof(string), typeof(IBrowsableAstNode));
			tvAST.HeadersVisible = false;
			tvAST.Model = modelAstTree;
		}

		private void SetupModel_tvParseTree ()
		{
			tvParseTree.AppendColumn ("ParseTree", new Gtk.CellRendererText (), "text", 0);
			TreeStore modelParseTree = new TreeStore (typeof(string), typeof(ParseTreeNode));
			tvParseTree.HeadersVisible = false;
			tvParseTree.Model = modelParseTree;
		}

		private void SetupModel_gridGrammarErrors ()
		{
			// err.Level.ToString(), err.Message, err.State)
			gridGrammarErrors.AppendColumn ("Error Level", new Gtk.CellRendererText (), "text", 0);
			gridGrammarErrors.AppendColumn ("Description", new Gtk.CellRendererText (), "text", 1);
			gridGrammarErrors.AppendColumn ("Parse State", new Gtk.CellRendererText (), "text", 2);
			ListStore modelGridGrammarErrors = new ListStore (typeof(string), typeof(string), typeof(string));
			gridGrammarErrors.Model = modelGridGrammarErrors;
		}

		private void SetupModel_gridCompileErrors ()
		{
			// err.Level.ToString(), err.Message, err.State)
			gridCompileErrors.AppendColumn ("L. C", new Gtk.CellRendererText (), "text", 0);
			gridCompileErrors.AppendColumn ("Error Message", new Gtk.CellRendererText (), "text", 1);
			ListStore modelGridCompileErrors = new ListStore (typeof(string), typeof(string), typeof(string));
			gridCompileErrors.Model = modelGridCompileErrors;
		}

		private void SetupModel_cboGrammars ()
		{
			// Setup the combobox to handle storing/display of GrammerItem class
			ListStore listStore = new Gtk.ListStore (typeof(GrammarItem), typeof(string));
			cboGrammars.Model = listStore;
			CellRendererText text = new CellRendererText (); 
			cboGrammars.PackStart (text, false); 
			cboGrammars.AddAttribute (text, "text", 1); 
		}

		wdgCellTable gridParserTrace;
		private void SetModel_gridParserTrace ()
		{
			gridParserTrace = new wdgCellTable (4);
			gridParserTrace.DefineHeader(new string[] {"State", "Stack Top", "Input", "Action"});
			swParseTrace.AddWithViewport(gridParserTrace);
			gridParserTrace.HeaderVisible = true;
			gridParserTrace.GridCellClicked += (object o, ButtonReleaseEventArgs args) =>  {OnParseStateHighLighted(o, args);};
		}

		private void SetModel_lstTokens ()
		{
			ListStore modelLstTokens = new ListStore (typeof(string), typeof(Token));
			lstTokens.AppendColumn ("Tokens", new Gtk.CellRendererText (), "text", 0, "foreground");
			lstTokens.Model = modelLstTokens;
		}

		private void SetupModel_btnManageGrammars ()
		{
//TODO
			// Setup the combobox to handle storing/display of Grammar Options and related function calls
			ListStore listStore = new Gtk.ListStore (typeof(string), typeof(System.Action));
			System.Action goselection = SelectGrammarAssembly;
			System.Action goselection2 = RemoveCurrentGrammar;
			System.Action goselection3 = RemoveAllGrammarsInList;
			listStore.AppendValues ("Load Grammars...", goselection);
			listStore.AppendValues ("Remove Selected", goselection2);
			listStore.AppendValues ("Remove All", goselection3);
			btnManageGrammars.Model = listStore;
			CellRendererText text = new CellRendererText (); 
			btnManageGrammars.PackStart (text, false); 
			// Only display the text column, not the function column
			btnManageGrammars.AddAttribute (text, "text", 0);
		}

		#region Form load/unload events

		private void fmExploreGrammarWindowLoad ()
		{
//			ClearLanguageInfo ();
			try {
				_teEditor.Text = MyApp.Settings.Default.SourceSample;
				txtSearch.Text = MyApp.Settings.Default.SearchPattern;
				GrammarItemList grammars = GrammarItemList.FromXml (MyApp.Settings.Default.Grammars);
				UpdateModelFromGrammerList (grammars, cboGrammars.Model as ListStore);
				chkParserTrace.Active = MyApp.Settings.Default.EnableTrace;
				chkDisableHili.Active = MyApp.Settings.Default.DisableHili;
				chkAutoRefresh.Active = MyApp.Settings.Default.AutoRefresh;

				TreeIter ti;
				cboGrammars.Model.GetIterFromString (out ti, MyApp.Settings.Default.LanguageIndex);
				if (!ti.Equals (null)) {
					cboGrammars.SetActiveIter (ti);
				}
			} catch {
			}
			_loaded = true;
		}

		private void fmExploreGrammarWindowClosing ()
		{
			MyApp.Settings.Default.SourceSample = _teEditor.Text;
			MyApp.Settings.Default.SearchPattern = txtSearch.Text;
			MyApp.Settings.Default.EnableTrace = chkParserTrace.Active;
			MyApp.Settings.Default.DisableHili = chkDisableHili.Active;
			MyApp.Settings.Default.AutoRefresh = chkAutoRefresh.Active;
			TreeIter ti;
			cboGrammars.GetActiveIter (out ti);
			MyApp.Settings.Default.LanguageIndex = cboGrammars.Model.GetStringFromIter (ti);

			Console.Write (cboGrammars.Model.GetStringFromIter (ti));
			var grammars = GetGrammarListFromModel (cboGrammars.Model as ListStore);
			MyApp.Settings.Default.Grammars = grammars.ToXml ();
			MyApp.Settings.Default.Save ();
		}

		private void UpdateModelFromGrammerList (GrammarItemList list, ListStore listStore)
		{
			// Following crashes when not on main Window thread, which makes sense:
			// listStore.Clear();
			// But even when using Gtk.Application.Invoke (delegate {}); to do it on the main UI thread... 
			// crash on GTK+ delegate, so hack it for now.
			ListStore newlistStore = new Gtk.ListStore (typeof(GrammarItem), typeof(string));
			cboGrammars.Model = newlistStore;
			// prevent the hack from leaking memory
			listStore.Dispose ();

			foreach (GrammarItem item in list) {
				newlistStore.AppendValues (item, item.Caption);
			}
		}

		private GrammarItemList GetGrammarListFromModel (ListStore listStore)
		{
			GrammarItemList list = new GrammarItemList ();
			foreach (Array item in listStore) {
				list.Add (item.GetValue (0) as GrammarItem);
			}
			return list;
		}

		#endregion

		#region Parsing and running

		private void CreateGrammar ()
		{
			_grammar = _grammarLoader.CreateGrammar ();
		}

		private void CreateParser ()
		{
			StopHighlighter ();
			btnRun.Sensitive = false;
			txtOutput.Buffer.Text = string.Empty;
			_parseTree = null;

			btnRun.Sensitive = _grammar is ICanRunSample;
			_language = new LanguageData (_grammar);
			_parser = new Parser (_language);
			ShowParserConstructionResults ();
			StartHighlighter ();
		}

		private void ParseSample ()
		{
			ClearParserOutput ();
			if (_parser == null || !_parser.Language.CanParse ())
				return;
			_parseTree = null;
			System.GC.Collect (); //to avoid disruption of perf times with occasional collections
			_parser.Context.TracingEnabled = chkParserTrace.Active;
			try {
				_parser.Parse (_teEditor.Text, "<source>");
			} catch (Exception ex) {
				(gridCompileErrors.Model as ListStore).AppendValues (null, ex.Message, null);
				tabBottom.CurrentPage = 2; //pageParserOutput;
				throw;
			} finally {
				_parseTree = _parser.Context.CurrentParseTree;
				ShowCompilerErrors ();
				if (chkParserTrace.Active) {
					ShowParseTrace ();
				}
				ShowCompileStats ();
				ShowParseTree ();
				ShowAstTree ();
			}
		}

		private void RunSample ()
		{
			ClearRuntimeInfo ();
			Stopwatch sw = new Stopwatch ();
			int oldGcCount;
			txtOutput.Buffer.Text = "";
			try {
				if (_parseTree == null)
					ParseSample ();
				if (_parseTree.ParserMessages.Count > 0)
					return;

				System.GC.Collect (); //to avoid disruption of perf times with occasional collections
				oldGcCount = System.GC.CollectionCount (0);
				System.Threading.Thread.Sleep (100);

				sw.Start ();
				var iRunner = _grammar as ICanRunSample;
				var args = new RunSampleArgs (_language, _teEditor.Text, _parseTree);
				string output = iRunner.RunSample (args);
				sw.Stop ();
				lblRunTime.Text = sw.ElapsedMilliseconds.ToString ();
				var gcCount = System.GC.CollectionCount (0) - oldGcCount;
				lblGCCount.Text = gcCount.ToString ();
				WriteOutput (output);
				tabBottom.CurrentPage = 4; //pageOutput;
			} catch (ScriptException ex) {
				ShowRuntimeError (ex);
			} finally {
				sw.Stop ();
			}//finally
		}

		private void WriteOutput (string text)
		{
			if (string.IsNullOrEmpty (text))
				return;
			txtOutput.Buffer.Text += text + Environment.NewLine;
//			txtOutput.Buffer.SelectRange( Select(txtOutput.Text.Length - 1, 0);
		}

		#endregion

		#region Show... methods

		private void ClearLanguageInfo ()
		{
			try {
				_txtTerms.Text = string.Empty;
				_txtNonTerms.Text = string.Empty;
				_txtParserStates.Text = string.Empty;
			} catch {
				// Skip errors on initial form load
			}
			lblLanguage.Text = string.Empty;
			lblLanguageVersion.Text = string.Empty;
			lblLanguageDescr.Text = string.Empty;
			txtGrammarComments.Buffer.Text = string.Empty;
		}

		private void ClearParserOutput ()
		{
			lblSrcLineCount.Text = string.Empty;
			lblSrcTokenCount.Text = "";
			lblParseTime.Text = "";
			lblParseErrorCount.Text = "";

			(lstTokens.Model as ListStore).Clear ();
			(gridCompileErrors.Model as ListStore).Clear ();
			gridParserTrace.ClearAll ();
			ClearTreeView (tvParseTree);
			ClearTreeView (tvAST);
		}

		private void ShowLanguageInfo ()
		{
			if (_grammar == null)
				return;
			var langAttr = LanguageAttribute.GetValue (_grammar.GetType ());
			if (langAttr == null)
				return;
			lblLanguage.Text = langAttr.LanguageName;
			lblLanguageVersion.Text = langAttr.Version;
			lblLanguageDescr.Text = langAttr.Description;
			txtGrammarComments.Buffer.Text = _grammar.GrammarComments;
		}

		private void ShowCompilerErrors ()
		{
			(gridCompileErrors.Model as ListStore).Clear ();
			if (_parseTree == null || _parseTree.ParserMessages.Count == 0)
				return;
			foreach (var err in _parseTree.ParserMessages)
				(gridCompileErrors.Model as ListStore).AppendValues (err.Location.ToUiString (), err.Message, err.ParserState.ToString ());
			var needPageSwitch = tabBottom.CurrentPage != 2 && //  pageParserOutput
			                     !(tabBottom.CurrentPage == 3 && chkParserTrace.Active);
			if (needPageSwitch)
				tabBottom.CurrentPage = 2; // pageParserOutput;
		}

		private void ShowParseTrace ()
		{
			gridParserTrace.ClearAll ();
			String cellColor;
			foreach (ParserTraceEntry entry in _parser.Context.ParserTrace) {
				if (entry.IsError) {
					cellColor = "red";
				} else {
					cellColor = "black";
				}
				// Getting strange Application/GTK crashes with NO/NONE/ZERO stack/exception details from assignment of some parse tree data (?) when 'AppendValues'.
				// (gridParserTrace.Model as ListStore).AppendValues((entry.State.Name), (entry.StackTop.ToString()), (entry.Input.ToString()), (entry.Message), cellColor );

				// MORE INFO: 'sometimes' GTK# is reporting (I REALLY HATE GTK# 2.1.2.... !!!!!!!):
				//   System.String[]System.String[]System.String[]System.String[]Marshaling clicked signal
				//		Exception in Gtk# callback delegate
				//			Note: Applications can use GLib.ExceptionManager.UnhandledException to handle the exception.
				// So 'bad' strings are being passed: GTK#->GTK/Glib
				// Much later....Found it...
				// Glib crash of accessing entry.Input when null;
				// Does not cause problems in Trace, Watch, Local, etc... but Mono/Glib fault
				// Why are they marshaling nulls??? and why does it fault Glib?
				string cInput = "";
				if (entry.Input != null) {
					cInput = _regexCleanWhiteSpace.Replace (entry.Input.ToString (), @" ");
				}
				string cStackTop = _regexCleanWhiteSpace.Replace (entry.StackTop.ToString (), @" ");

//				string cleanedup = System.Text.RegularExpressions.Regex.Replace(entry.StackTop.ToString(),@"\s+"," ");
//				(gridParserTrace.Model as ListStore).AppendValues ((entry.State.Name), (cleanedup), inputNoNullString, (entry.Message), cellColor);
				// "State", "Stack Top", "Input", "Action"}
				gridParserTrace.AddRow (new CellData (entry.State.Name), new CellData (cStackTop) , new CellData (cInput), new CellData (entry.Message));
//				(gridParserTrace.Model as ListStore).AppendValues(foobar[0], foobar[1], foobar[2], foobar[3], cellColor);
			}
			//Show tokens
			String foo;
			foreach (Token tkn in _parseTree.Tokens) {
				if (chkExcludeComments.Active && tkn.Category == TokenCategory.Comment)
					continue;
				foo = tkn.ToString ();
				//TODO
//				System.Environment.NewLine
//				foo = System.Text.RegularExpressions.Regex.Replace (foo.Trim(), @"\n+", " ");
				string cleanedup = System.Text.RegularExpressions.Regex.Replace (foo, @"\s+", " ");
				(lstTokens.Model as ListStore).AppendValues ( cleanedup, tkn );
			}
		}

		private void ShowCompileStats ()
		{
			if (_parseTree != null) {
				lblSrcLineCount.Text = string.Empty;
				if (_parseTree.Tokens.Count > 0)
					lblSrcLineCount.Text = (_parseTree.Tokens [_parseTree.Tokens.Count - 1].Location.Line + 1).ToString ();
				lblSrcTokenCount.Text = _parseTree.Tokens.Count.ToString ();
				lblParseTime.Text = _parseTree.ParseTimeMilliseconds.ToString ();
				lblParseErrorCount.Text = _parseTree.ParserMessages.Count.ToString ();
//	     		Application.DoEvents();
			}
			//Note: this time is "pure" parse time; actual delay after cliking "Compile" includes time to fill ParseTree, AstTree controls
		}

		private void ClearTreeView (TreeView tv)
		{
			TreeStore ts = (tv.Model as TreeStore);
			if (ts.IterNChildren () > 0) {
				// HACK: Gtk delegate threading issue again even on Application.Invoke....
				tv.Model = null;
				ts.Clear ();
				tv.Model = ts;
			}
		}

		private void ShowParseTree ()
		{
			ClearTreeView (tvParseTree);
			if (_parseTree != null) {
				AddParseNodeRec (TreeIter.Zero, _parseTree.Root);
			}
		}

		private void AddParseNodeRec (TreeIter parent, ParseTreeNode node)
		{
			if (node != null) {
				string txt = _regexCleanWhiteSpace.Replace (node.ToString (), @" ");

				TreeIter ti;
				TreeStore ptree = tvParseTree.Model as TreeStore;
				if (!parent.Equals (TreeIter.Zero)) {
					ti = ptree.AppendValues (parent, txt, node);
				} else {
					ti = ptree.AppendValues (txt);
				}
				foreach (var child in node.ChildNodes)
					AddParseNodeRec (ti, child);
			}
		}

		private void ShowAstTree ()
		{
			ClearTreeView (tvAST);
			if (_parseTree == null || _parseTree.Root == null || _parseTree.Root.AstNode == null)
				return;
			AddAstNodeRec (TreeIter.Zero, _parseTree.Root.AstNode);
		}

		private void AddAstNodeRec (TreeIter parent, object astNode)
		{
			if (astNode != null) {
				string txt = _regexCleanWhiteSpace.Replace (astNode.ToString (), @" ");

				TreeIter ti;
				TreeStore asttree = tvAST.Model as TreeStore;
				if (!parent.Equals (TreeIter.Zero)) {
					ti = asttree.AppendValues (parent, txt, astNode);
				} else {
					ti = asttree.AppendValues (txt);
				}
				var iBrowsable = astNode as IBrowsableAstNode;
				if (iBrowsable != null) {
					var childList = iBrowsable.GetChildNodes ();
					foreach (var child in childList)
						AddAstNodeRec (ti, child);
				}
			}
		}

		private void ShowParserConstructionResults ()
		{
			lblParserStateCount.Text = _language.ParserData.States.Count.ToString ();
			lblParserConstrTime.Text = _language.ConstructionTime.ToString ();
			(gridGrammarErrors.Model as ListStore).Clear ();
			try {
				_txtTerms.Text = string.Empty;
				_txtNonTerms.Text = string.Empty;
				_txtParserStates.Text = string.Empty;
			} catch {
				// Due to form creation order, this editors might not be created
			}
			tabBottom.CurrentPage = 0; // pageLanguage;
			if (_parser != null) {
				_txtTerms.Text = ParserDataPrinter.PrintTerminals (_language);
				_txtTerms.Document.ReadOnly = true;
				_txtNonTerms.Text = ParserDataPrinter.PrintNonTerminals (_language);
				_txtNonTerms.Document.ReadOnly = true;
				_txtParserStates.Text = ParserDataPrinter.PrintStateList (_language);
				_txtNonTerms.Document.ReadOnly = true;
				ShowGrammarErrors ();
			}
		}

		private void ShowGrammarErrors ()
		{
			(gridGrammarErrors.Model as ListStore).Clear ();
			var errors = _parser.Language.Errors;
			if (errors.Count == 0)
				return;
			foreach (var err in errors)
				(gridGrammarErrors.Model as ListStore).AppendValues (err.Level.ToString (), err.Message, err.State);
			if (tabBottom.CurrentPage != 1) // pageGrammarErrors
				tabBottom.CurrentPage = 0;
		}

		private void ShowSourcePosition (TextEditor te, Irony.Parsing.SourceSpan sSpan)
		{
			ShowSourcePosition (te, sSpan.Location.Line, sSpan.Location.Column, sSpan.Length);
		}

		private void ShowSourcePosition (TextEditor te, ScriptException scriptException)
		{
			ShowSourcePosition (te, scriptException.Location.Line, 0, 0);
		}

		private void ShowSourcePosition (TextEditor te, int line, int column, int length)
		{
			if (te.IsSomethingSelected) {
				te.ClearSelection ();
			}
			if (length == 0) {
				te.SetCaretTo (line + 1, 1);
				te.StartCaretPulseAnimation ();
				te.SetSelectLines (line + 1, line + 1);
			} else {
				te.SetCaretTo (line + 1, column + 1);
				te.StartCaretPulseAnimation ();
				te.SetSelection (line + 1, column + 1, line + 1, column + length + 1);
			}
			if (tabGrammar.CurrentPage != 3)
				tabGrammar.CurrentPage = 3;
			te.GrabFocus ();
		}

		private void ClearTraceTokenSelection ()
		{
			(lstTokens.NodeSelection).UnselectAll ();
		}

		private void ShowTraceToken (int position, int length)
		{
			TreeIter ti;
			ListStore modelLstTokens = lstTokens.Model as ListStore;
			for (int i = 0; i < modelLstTokens.IterNChildren(); i++) {
				lstTokens.Model.GetIterFromString(out ti, i.ToString() );
				Token token = modelLstTokens.GetValue (ti, 1) as Token;
				if (!token.Equals (null)) {
					if (token.Location.Position == position) {
						lstTokens.SetCursor (new TreePath (i.ToString ()), lstTokens.Columns [0], false);
						return;
					}
				}
			}
		}

		private void LocateParserState (ParserState state)
		{
			if (state == null) return;
			if (tabGrammar.CurrentPage != 2)
				tabGrammar.CurrentPage = 2;
			DoSearch(_txtParserStates, "State " + state.Name, 0);
		}

		private void ShowRuntimeError (ScriptException error)
		{
			_runtimeError = error;
			lnkShowErrLocation.Sensitive = _runtimeError != null;
			lnkShowErrStack.Sensitive = lnkShowErrLocation.Sensitive;
			if (_runtimeError != null) {
				//the exception was caught and processed by Interpreter
				WriteOutput ("Error: " + error.Message + " At " + _runtimeError.Location.ToUiString () + ".");
//				ShowSourcePosition(_runtimeError.Location.Position, 1);
			} else {
				//the exception was not caught by interpreter/AST node. Show full exception info
				WriteOutput ("Error: " + error.Message);
				dlgShowException showExceptionDialog = new dlgShowException (error.Message);
				showExceptionDialog.Destroy ();
			}
			tabBottom.CurrentPage = 4; // pageOutput;
		}

		private void SelectTreeNode (TreeView tree, TreeNode node)
		{
//			_treeClickDisabled = true;
//			tree.SelectedNode = node;
//			if (node != null)
//				node.EnsureVisible();
//			_treeClickDisabled = false;
		}

		private void ClearRuntimeInfo ()
		{
			lnkShowErrLocation.Sensitive = false;
			lnkShowErrStack.Sensitive = false;
			_runtimeError = null;
			txtOutput.Buffer.Text = string.Empty;
		}

		#endregion

		#region Grammar combo menu commands

		private void RemoveCurrentGrammar ()
		{
//			if (MessageBox.Show("Are you sure you want to remove grammmar " + cboGrammars.SelectedItem + "?",
//			                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
//				cboGrammars.Items.RemoveAt(cboGrammars.SelectedIndex);
			_parser = null;
			if ((cboGrammars.Model as ListStore).IterNChildren () > 0) {
				TreeIter ti;
				cboGrammars.GetActiveIter (out ti);
				// Temp removal of ListStore from combobox as removing items while active is not threadsafe in GTK
				ListStore tmpListStore = cboGrammars.Model as ListStore;
				cboGrammars.Model = null;
				tmpListStore.Remove (ref ti);
				tmpListStore.GetIterFirst (out ti);
				cboGrammars.Model = tmpListStore;
				cboGrammars.SetActiveIter (ti);
				btnRefresh.Sensitive = true;
				if (tmpListStore.IterNChildren () == 0) {
					ClearUIInfo ();
				}
			} else {
				btnRefresh.Sensitive = false;
			}
		}

		private void RemoveAllGrammarsInList ()
		{
//			if (MessageBox.Show("Are you sure you want to remove all grammmars in the list?",
//			                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
			_parser = null;
			ListStore tmpListStore = cboGrammars.Model as ListStore;
			cboGrammars.Model = null;
			tmpListStore.Clear ();
			cboGrammars.Model = tmpListStore;
			ClearUIInfo ();
			btnRefresh.Sensitive = false;
//			}
		}

		#endregion

		private void SelectGrammarAssembly ()
		{
			Gtk.FileChooserDialog fc =
				new Gtk.FileChooserDialog ("Choose the Irony-based grammar to open",
				                           this,
				                           FileChooserAction.Open,
				                           "Cancel", ResponseType.Cancel,
				                           "Open", ResponseType.Accept);
			fc.Run ();
			string location = fc.Filename;
			if (!string.IsNullOrEmpty (location)) {
				var oldGrammars = new GrammarItemList ();
				fc.Destroy ();
				SelectGrammars (location, oldGrammars);
			} else {
				fc.Destroy ();
			}
		}

		protected void OnBtnRefreshClicked (object sender, EventArgs e)
		{
			LoadSelectedGrammar ();
		}

		protected void SelectGrammars (string filename, GrammarItemList grammerlist)
		{
			dlgSelectGrammars grammarListDialog = new dlgSelectGrammars ();
			grammarListDialog.ShowGrammars (filename, grammerlist, new dlgSelectGrammars.ProcessGrammars (foobar), this);
		}

		private void ClearUIInfo ()
		{
			ClearLanguageInfo ();
			ClearParserOutput ();
			ClearRuntimeInfo ();
		}
		//		public delegate void ProcessBookDelegate (GrammarItemList grammarlist);
		public void foobar (GrammarItemList grammarlist)
		{
			if (grammarlist != null) {
				// Store the Grammer items from the dll in the combobox ListStore model
				UpdateModelFromGrammerList (grammarlist, cboGrammars.Model as ListStore);
				btnRefresh.Sensitive = false;
				// auto-select the first grammar if no grammar currently selected
				TreeIter ti;
				cboGrammars.Model.GetIterFirst (out ti);
				cboGrammars.SetActiveIter (ti);
			} 
			if (grammarlist.Count == 0) {
				ClearUIInfo ();
			}
		}

		#region miscellaneous: LoadSourceFile, Search, Source highlighting

		private void LoadSourceFile (string path)
		{
			_parseTree = null;
			StreamReader reader = null;
			try {
				reader = new StreamReader (path);
				_teEditor.Text = String.Empty;  //to clear any old formatting
//				txtSource.ClearUndo();
//				txtSource.ClearStylesBuffer();
				_teEditor.Text = reader.ReadToEnd ();
//				txtSource.SetVisibleState(0, FastColoredTextBoxNS.VisibleState.Visible);
//				txtSource.Selection = txtSource.GetRange(0, 0);
			} catch (Exception error) {
				dlgShowException showExceptionDialog = new dlgShowException (error.Message);
				showExceptionDialog.Destroy ();
			} finally {
				if (reader != null)
					reader.Close ();
			}
		}

		private void StartHighlighter ()
		{
			_IronySyntaxMode = new IronySyntaxMode (null);
			Mono.TextEditor.Highlighting.SyntaxModeService.InstallSyntaxMode ("text/x-irony", new SyntaxModeProvider (doc => _IronySyntaxMode ));
			_IronySyntaxMode.AddKeywords ();
			_teEditor.Document.MimeType = "text/x-irony";
		}

		private void StopHighlighter ()
		{
			// TODO : Disable IronySyntaxMode
			// Either remove the SyntaxMode or just clear keywords?
			ClearHighlighting ();
			// Need to refresh the viewable text the mono.editor
		}

		private void ClearHighlighting ()
		{
			var txt = _teEditor.Text;
			_teEditor.Text = String.Empty;
			_teEditor.Text = txt;
		}

		private void EnableHighlighter (bool enable)
		{
			if (enable)
				StartHighlighter ();
			else
				StopHighlighter();
		}

		private void DoSearch ()
		{
			_teEditor.ClearSelection ();
			_teEditor.HighlightSearchPattern = true;
			_teEditor.SearchPattern = txtSearch.Text;
			Mono.TextEditor.SearchResult foo = _teEditor.SearchForward (0);
			lblSearchError.Visible = (foo == null);
			if (foo != null) {
				_teEditor.AnimateSearchResult (foo);
			}
		}

		private bool DoSearch(TextEditor te, string fragment, int start) 
		{
			te.ClearSelection ();
			te.HighlightSearchPattern = true;
			te.SearchPattern = fragment;
			Mono.TextEditor.SearchResult found = te.SearchForward (0);

			lblSearchError.Visible = (found == null);
			if (found != null) {
				te.StopSearchResultAnimation ();
				te.Caret.Location = te.OffsetToLocation (found.EndOffset);
				te.SetSelection (found.Offset, found.EndOffset);
				te.CenterToCaret ();
				te.AnimateSearchResult (found);
				return true;
			} else {
				return false;
			}
		}

		#endregion

		#region Controls event handlers

		protected void OnBtnParseClicked (object sender, EventArgs e)
		{
			ParseSample ();
		}

		protected void OnBtnRunClicked (object sender, EventArgs e)
		{
			RunSample ();
		}
		//		private void tvParseTree_AfterSelect(object sender, TreeViewEventArgs e) {
		//			if (_treeClickDisabled)
		//				return;
		//			var vtreeNode = tvParseTree.SelectedNode;
		//			if (vtreeNode == null) return;
		//			var parseNode = vtreeNode.Tag as ParseTreeNode;
		//			if (parseNode == null) return;
		//			ShowSourcePosition(parseNode.Span.Location.Position, 1);
		//		}
		//
		//		private void tvAst_AfterSelect(object sender, TreeViewEventArgs e) {
		//			if (_treeClickDisabled)
		//				return;
		//			var treeNode = tvAst.SelectedNode;
		//			if (treeNode == null) return;
		//			var iBrowsable = treeNode.Tag as IBrowsableAstNode;
		//			if (iBrowsable == null) return;
		//			ShowSourcePosition(iBrowsable.Position, 1);
		//		}
		bool _changingGrammar;

		private void LoadSelectedGrammar ()
		{
			try {
				ClearLanguageInfo ();
				ClearParserOutput ();
				ClearRuntimeInfo ();

				_changingGrammar = true;
				CreateGrammar ();
				ShowLanguageInfo ();
				CreateParser ();
			} finally {
				_changingGrammar = false; //in case of exception
			}
			btnRefresh.Sensitive = true;
		}

		protected void OnCboGrammarsChanged (object sender, EventArgs e)
		{
			TreeIter ti;
			cboGrammars.GetActiveIter (out ti);
			_grammarLoader.SelectedGrammar = cboGrammars.Model.GetValue (ti, 0) as GrammarItem;
			LoadSelectedGrammar ();
		}

		private void GrammarAssemblyUpdated (object sender, EventArgs args)
		{
			if (chkAutoRefresh.Active) {
				Gtk.Application.Invoke (delegate {
					LoadSelectedGrammar ();
					txtGrammarComments.Buffer.Text += String.Format ("{0}Grammar assembly reloaded: {1:HH:mm:ss}", Environment.NewLine, DateTime.Now);
				});
			}
		}

		private void btnRefresh_Click (object sender, EventArgs e)
		{
			LoadSelectedGrammar ();
		}

		protected void OnFcbtnFileOpenSelectionChanged (object sender, EventArgs e)
		{
			FileChooserButton file = sender as FileChooserButton;
			string location = file.Filename;
			if (!string.IsNullOrEmpty (location)) {
				ClearParserOutput ();
				LoadSourceFile (location);
			}
		}
		//TODO
		//		private void txtSource_TextChanged(object sender, FastColoredTextBoxNS.TextChangedEventArgs e) {
		//			_parseTree = null; //force it to recompile on run
		//		}
		//		private void btnManageGrammars_Click(object sender, EventArgs e) {
		//			menuGrammars.Show(btnManageGrammars, 0, btnManageGrammars.Height);
		//		}
		private void cboParseMethod_SelectedIndexChanged (object sender, EventArgs e)
		{
			//changing grammar causes setting of parse method combo, so to prevent double-call to ConstructParser
			// we don't do it here if _changingGrammar is set
			if (!_changingGrammar)
				CreateParser ();
		}
		//		private void gridParserTrace_CellDoubleClick(object sender, DataGridViewCellEventArgs e) {
		//			if (_parser.Context == null || e.RowIndex < 0 || e.RowIndex >= _parser.Context.ParserTrace.Count) return;
		//			var entry = _parser.Context.ParserTrace[e.RowIndex];
		//			switch (e.ColumnIndex) {
		//			case 0: //state
		//			case 3: //action
		//				LocateParserState(entry.State);
		//				break;
		//			case 1: //stack top
		//				if (entry.StackTop != null)
		//					ShowSourcePositionAndTraceToken(entry.StackTop.Span.Location.Position, entry.StackTop.Span.Length);
		//				break;
		//			case 2: //input
		//				if (entry.Input != null)
		//					ShowSourcePositionAndTraceToken(entry.Input.Span.Location.Position, entry.Input.Span.Length);
		//				break;
		//			}//switch
		//		}
		//		
		//		private void gridCompileErrors_CellDoubleClick(object sender, DataGridViewCellEventArgs e) {
		//			if (e.RowIndex < 0 || e.RowIndex >= gridCompileErrors.Rows.Count) return;
		//			var err = gridCompileErrors.Rows[e.RowIndex].Cells[1].Value as LogMessage;
		//			switch (e.ColumnIndex) {
		//			case 0: //state
		//			case 1: //stack top
		//				ShowSourcePosition(err.Location.Position, 1);
		//				break;
		//			case 2: //input
		//				if (err.ParserState != null)
		//					LocateParserState(err.ParserState);
		//				break;
		//			}//switch
		//		}
		//		private void gridGrammarErrors_CellDoubleClick(object sender, DataGridViewCellEventArgs e) {
		//			if (e.RowIndex < 0 || e.RowIndex >= gridGrammarErrors.Rows.Count) return;
		//			var state = gridGrammarErrors.Rows[e.RowIndex].Cells[2].Value as ParserState;
		//			if (state != null)
		//				LocateParserState(state);
		//		}
		//		private void btnSearch_Click(object sender, EventArgs e) {
		//			DoSearch();
		//		}//method
		protected void OnBtnSearchClicked (object sender, EventArgs e)
		{
			DoSearch ();
		}

		protected void OnDefaultActivated (object sender, EventArgs e)
		{
			(sender as Gtk.Window).Present ();
		}

		protected void OnRealized (object sender, EventArgs e)
		{
			(sender as Gtk.Window).Present ();
		}

		protected void OnStateChanged (object sender, EventArgs e)
		{
			Debug.Write ("window state change");
			_fullScreen = !_fullScreen;
		}

		protected void OnMinimizeActionActivated (object sender, EventArgs e)
		{
			this.Iconify ();
		}

		protected void OnOpenGrammarAssemblyActionActivated (object sender, EventArgs e)
		{
			SelectGrammarAssembly ();
		}

		protected void OnEnterFullScreenActionActivated (object sender, EventArgs e)
		{
			if (!_fullScreen) {
				this.Fullscreen ();
				EnterFullScreenAction.Label = "Exit Full Screen";
			} else {
				this.Unfullscreen ();	
				EnterFullScreenAction.Label = "Enter Full Screen";
			}
			_fullScreen = !_fullScreen;
		}

		protected void OnCloseGrammarAssemblyActionActivated (object sender, EventArgs e)
		{
			RemoveAllGrammarsInList ();
		}

		protected void OnQuitActionActivated (object sender, EventArgs e)
		{
			Application.Quit ();
		}

		protected void OnAboutIrontGrammarExplorerActionActivated (object sender, EventArgs e)
		{
			AboutDialog about = new AboutDialog ();
			about.Parent = this;
			about.SetPosition (WindowPosition.CenterOnParent);
			Assembly currentAssem = typeof(MainWindow).Assembly;
			object[] attribs = currentAssem.GetCustomAttributes (typeof(AssemblyCompanyAttribute), true);
			attribs = currentAssem.GetCustomAttributes (typeof(AssemblyDescriptionAttribute), true);
			if (attribs.Length > 0)
				about.ProgramName = ((AssemblyDescriptionAttribute)attribs [0]).Description;
			attribs = currentAssem.GetCustomAttributes (typeof(AssemblyFileVersionAttribute), true);
			if (attribs.Length > 0)
				about.Version = ((AssemblyFileVersionAttribute)attribs [0]).Version;
			string[] authors = { "GtkSharp version by Robert Nees", "Original Windows.Forms Version by Roman Ivantsov" };
			attribs = currentAssem.GetCustomAttributes (typeof(AssemblyCopyrightAttribute), true);
			if (attribs.Length > 0)
				about.Copyright = ((AssemblyCopyrightAttribute)attribs [0]).Copyright;
			about.Authors = authors;
			// Note: Link will only work when run from within App bundle on OS-X
			about.Website = "http://irony.codeplex.com";
			about.Response += (object o, ResponseArgs args) => about.Destroy ();
			about.Show ();
		}

		protected void OnTxtSearchChanged (object sender, EventArgs e)
		{
			btnSearch.Sensitive = (txtSearch.Text != String.Empty);
			DoSearch ();
		}

		bool _InManageGrammarSelectiion = false;
		// HACK: no context menu like winform, using a combobox instead.
		// and need to ignore 'second' change event when clearing the selection after processing user's selection
		protected void OnBtnManageGrammarsChanged (object sender, EventArgs e)
		{
			if (!_InManageGrammarSelectiion) {
				_InManageGrammarSelectiion = true;
				try {
					string _manageGrammar;
					TreeIter ti;
					btnManageGrammars.GetActiveIter (out ti);
					ListStore listStore = btnManageGrammars.Model as ListStore;
					_manageGrammar = listStore.GetValue (ti, 0) as string;
					Console.WriteLine (_manageGrammar);
					(listStore.GetValue (ti, 1) as System.Action) ();
				} finally {
					btnManageGrammars.SetActiveIter (TreeIter.Zero);
					_InManageGrammarSelectiion = false;
				}
			}
		}

		protected void OnTxtSearchEditingDone (object sender, EventArgs e)
		{
			DoSearch ();
		}

		protected void OnBtnLocateClicked (object sender, EventArgs e)
		{
			if (_parser != null) {
				if (_teEditor.IsSomethingSelected) {
					if (_parseTree == null) {
						ParseSample ();
					}
					TextSegment segment = _teEditor.SelectionRange;

					LocateTreeNode2 (tvParseTree, segment.Offset, segment.Length);
					tvParseTree.CollapseAll ();
					tvParseTree.ExpandToPath (_currentPath);
					var treeselect = tvParseTree.Selection;
					tvParseTree.SetCursor (_currentPath, tvParseTree.Columns [0], false);

					LocateTreeNode2 (tvAST, segment.Offset, segment.Length);
					tvAST.CollapseAll ();
					tvAST.ExpandToPath (_currentPath);
					treeselect = tvParseTree.Selection;
					tvAST.SetCursor (_currentPath, tvAST.Columns [0], false);
				}
			}
		}

		protected void OnLnkShowErrStackClicked (object sender, EventArgs e)
		{
			if (_runtimeError != null) {
				if (_runtimeError.InnerException != null) {
					dlgShowException showExceptionDialog = new dlgShowException (_runtimeError.InnerException.ToString ());
					showExceptionDialog.Response += (object o, ResponseArgs args) => showExceptionDialog.Destroy ();
				} else {
					dlgShowException showExceptionDialog = new dlgShowException (_runtimeError.ToString ());
					showExceptionDialog.Response += (object o, ResponseArgs args) => showExceptionDialog.Destroy ();
				}
			}
		}

		protected void OnLnkShowErrLocationClicked (object sender, EventArgs e)
		{
			if (_runtimeError != null)
				ShowSourcePosition (_teEditor, _runtimeError);
		}

		protected void OnTvParseTreeRowActivated (object o, RowActivatedArgs args)
		{
			TreeIter ti;
			(tvParseTree.Model as TreeStore).GetIter (out ti, args.Path);
			ParseTreeNode parseNode = (tvParseTree.Model as TreeStore).GetValue (ti, 1) as ParseTreeNode;
			if (parseNode != null) {
				ShowSourcePosition (_teEditor, parseNode.Span);
			}
		}

		protected void OnTvASTRowActivated (object o, RowActivatedArgs args)
		{
			TreeIter ti;
			(tvAST.Model as TreeStore).GetIter (out ti, args.Path);
			Irony.Interpreter.Ast.AstNode astNode = (tvAST.Model as TreeStore).GetValue (ti, 1) as Irony.Interpreter.Ast.AstNode;
			if (astNode != null) {
				ShowSourcePosition (_teEditor, astNode.Span);
			}
		}

		#endregion

		TreeIter _currentNode;
		TreePath _currentPath;
		int _start;
		int _end;

		private bool ForEachTreeNode (Gtk.TreeModel model, Gtk.TreePath path, Gtk.TreeIter iter)
		{
			ParseTreeNode foo = model.GetValue (iter, 1) as ParseTreeNode;
			if (foo == null)
				return false;
			Irony.Parsing.SourceSpan sSpan = foo.Span;
			if (sSpan.Location.Position >= _start) 
			{
				if (_end == sSpan.Length) {
					_currentNode = iter;
					_currentPath = path.Copy ();
					return true;
				}
			}
			return false;
		}

		private TreeIter LocateTreeNode2 (TreeView tv, int start, int end)
		{
			_currentNode = TreeIter.Zero;
			_start = start;
			_end = end;
			tv.Model.Foreach (new TreeModelForeachFunc (ForEachTreeNode));
			return _currentNode;
		}

//		[GLib.ConnectBefore]
//		protected void OnTvParseTreeButtonPressEvent (object o, ButtonPressEventArgs args)
//		{
//			TreePath path;
//			int x = Convert.ToInt32(args.Event.X);
//			int y = Convert.ToInt32(args.Event.Y);
//			if (!(o as TreeView).GetPathAtPos (x, y, out path)) 
//				return;
//			Console.WriteLine("@@@@@");
//			Console.WriteLine(path);
//			Console.WriteLine("@@@@@");
//		}

		private void OnParseStateHighLighted (object o, ButtonReleaseEventArgs args)
		{
			Label tmpLbl = (((o as EventBox).Child) as Label);
			var rowcolumn = new CellRowColumn( Convert.ToInt32(tmpLbl.Name.ToString().Split('_')[1]) - 1, Convert.ToInt32(tmpLbl.Name.ToString().Split('_')[0]) - 1);
			Console.WriteLine (rowcolumn.RowIndex.ToString () + " " + rowcolumn.ColumnIndex.ToString ());
			if (_parser.Context == null || rowcolumn.RowIndex < 0 || rowcolumn.RowIndex >= _parser.Context.ParserTrace.Count) return;
			var entry = _parser.Context.ParserTrace[rowcolumn.RowIndex];
			switch (rowcolumn.ColumnIndex) {
			case 0: //state
			case 3: //action
				LocateParserState(entry.State);
				break;
			case 1: //stack top
				if (entry.StackTop != null) {
					ShowSourcePosition (_teEditor, entry.StackTop.Span);
					if (entry.Input != null) {
						ShowTraceToken (entry.Input.Span.Location.Position, entry.Input.Span.Length);
					} else {
						ClearTraceTokenSelection ();
					}
				}
				break;
			case 2: //input
				if (entry.Input != null) {
					ShowSourcePosition (_teEditor, entry.StackTop.Span);
					if (entry.Input != null) {
						ShowTraceToken (entry.Input.Span.Location.Position, entry.Input.Span.Length);
					} else {
						ClearTraceTokenSelection ();
					}
				}
				break;
			}
		}

		protected void OnLstTokensRowActivated (object o, RowActivatedArgs args)
		{
			TreeIter ti;
			ListStore modelLstTokens = (o as NodeView).Model as ListStore;
			modelLstTokens.GetIter (out ti, args.Path);
			if (!ti.Equals (null)) {
				Token token = modelLstTokens.GetValue (ti, 1) as Token;
				if (!token.Equals(null)) {
					ShowSourcePosition (_teEditor, token.Location.Line, token.Location.Column, token.Length);
				}
			}
		}

		protected void OnChkDisableHiliToggled (object sender, EventArgs e)
		{
			if (_loaded) {
				EnableHighlighter (!chkDisableHili.Active);
			}
		}
	}
}



