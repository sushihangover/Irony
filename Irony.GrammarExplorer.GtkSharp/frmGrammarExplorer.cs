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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Linq;
using Mono.TextEditor;
using Mono.TextEditor.Highlighting;
using Medsphere.Widgets;
using Pinta.Core;
using IgeMacIntegration;
using Irony.Ast;
using Irony.Parsing;
using Irony.GrammarExplorer;
using Irony.Interpreter;

namespace Irony.GrammarExplorer
{
	// Settings conflict with Gtk.Settings
	using MyApp = Irony.GrammarExplorer.Properties;
	
	public partial class MainWindow: Gtk.Window
	{
		// UI vars
		private bool _fullScreen;
		private GridView _gridCompileErrors;
		private GridView  _gridParserTrace;
		private TextEditor _teEditor;
		private TextEditor _txtTerms;
		private TextEditor _txtNonTerms;
		private TextEditor _txtParserStates;
		// Mono.TextEditor Syntax Modes (XML resources plus the addition of dymanic keywords
		private IronySyntaxMode _IronySyntaxMode;
		private IronyParserStatesSyntaxMode _IronyParserStatesSyntaxMode;

		private System.Text.RegularExpressions.Regex _regexCleanWhiteSpace = new System.Text.RegularExpressions.Regex (@"			[ ]{2,}", RegexOptions.None);

		// Irony vars
		private Grammar _grammar;
		private LanguageData _language;
		private Parser _parser;
		private ParseTree _parseTree;
		private ScriptException _runtimeError;
		private GrammarLoader _grammarLoader = new GrammarLoader ();
		bool _loaded;

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
				TextEditorOptions overrideOptions = new TextEditorOptions ();
				_teEditor.Options = overrideOptions;
				_teEditor.Options.ColorScheme = "Default";
				_teEditor.Options.ShowIconMargin = false;
				_teEditor.Options.ShowFoldMargin = false;
				_teEditor.Options.ShowRuler = false;
				_teEditor.Options.WrapLines = true;

				_teEditor.SelectionChanged += (object sender, EventArgs e) => OnBtnLocateClicked(sender, e);

				swEditor.ShowAll ();
				sWinTerminals.Child = _txtTerms;
				sWinTerminals.ShowAll ();
				_txtTerms.Document.ReadOnly = true;
				// ColorScheme of the TextEditor.Options = "Default, TangoLight, Visual Studio, IrBlack, GEdit, Brown, C64, Oblivion"
				_txtTerms.Options.ColorScheme = "Default";
				_txtTerms.Options.ShowIconMargin = false;
				_txtTerms.Options.ShowFoldMargin = false;
				_txtTerms.Options.ShowRuler = false;

				sWinNonTerminals.Child = _txtNonTerms;
				sWinNonTerminals.ShowAll ();
				_txtNonTerms.Document.ReadOnly = true;

				sWinParserStates.Child = _txtParserStates;
				sWinParserStates.ShowAll ();
				_txtParserStates.Document.ReadOnly = true;
				StartParserStatesHighlighter();
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
			if (SystemManager.GetOperatingSystem() == OS.Mac) {
				mbExplorer.Hide ();
				IgeMacMenu.GlobalKeyHandlerEnabled = true;
				IgeMacMenu.MenuBar = mbExplorer;
				// TOOD Fix the quit action via menu to call proper event so user.config is updated 
				IgeMacMenu.QuitMenuItem = QuitAction;
			}
		}

		protected void OnDeleteEvent (object sender, DeleteEventArgs a)
		{
			fmExploreGrammarWindowClosing ();
			Application.Quit ();
			a.RetVal = true;
		}

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
			gridGrammarErrors.AppendColumn ("State", new Gtk.CellRendererText (), "text", 2);
			ListStore modelGridGrammarErrors = new ListStore (typeof(string), typeof(string), typeof(string));
			gridGrammarErrors.Model = modelGridGrammarErrors;
		}

		private void SetModel_gridParserTrace ()
		{
			int n_columns = 4;
			ListStore modelParserTrace = new ListStore (typeof(string), typeof(string), typeof(string),  typeof(string), typeof(LogMessage));

			_gridParserTrace = new GridView ();
			_gridParserTrace.HscrollbarPolicy = PolicyType.Automatic;
			_gridParserTrace.VscrollbarPolicy = PolicyType.Automatic;
			swParseTrace.AddWithViewport(_gridParserTrace);
			_gridParserTrace.CellPressEvent += (object o, CellPressEventArgs args) => {
				Debug.Print("Parser State CELL: {0} {1}", args.Column, args.Row);
				OnParseStateCellClicked(o, args);
			};
			for (int x = 0; x < n_columns; x++) {
				CellRendererText renderer = new CellRendererText ();
				_gridParserTrace.AppendColumn (renderer, "Text", x);
			}
			_gridParserTrace.Model = modelParserTrace;
		}

		private void SetupModel_gridCompileErrors ()
		{
			int n_columns = 3;
			ListStore modelGridCompileErrors = new ListStore (typeof(string), typeof(string), typeof(string), typeof(LogMessage));

			_gridCompileErrors = new GridView ();
			_gridCompileErrors.HscrollbarPolicy = PolicyType.Automatic;
			_gridCompileErrors.VscrollbarPolicy = PolicyType.Automatic;
			swCompileErrors.AddWithViewport (_gridCompileErrors);
			_gridCompileErrors.CellPressEvent += (object o, CellPressEventArgs args) => {
				Debug.Print("CELL: {0} {1}", args.Column, args.Row);
				OnGridCompileErrorsClick(o, args);
			};

			for (int x = 0; x < n_columns; x++) {
				CellRendererText renderer = new CellRendererText ();
				_gridCompileErrors.AppendColumn (renderer, "Text", x);
			}
			_gridCompileErrors.Model = modelGridCompileErrors;
		}

		private void SetupModel_cboGrammars ()
		{
			// Setup the combobox to handle storing/display of GrammarItem class
			ListStore listStore = new Gtk.ListStore (typeof(GrammarItem), typeof(string));
			cboGrammars.Model = listStore;
			CellRendererText text = new CellRendererText (); 
			cboGrammars.PackStart (text, false); 
			cboGrammars.AddAttribute (text, "text", 1); 
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
				UpdateModelFromGrammarList (grammars, cboGrammars.Model as ListStore);
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

		private void UpdateModelFromGrammarList (GrammarItemList list, ListStore listStore)
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
			StopTestSourceHighlighter ();
			btnRun.Sensitive = false;
			txtOutput.Buffer.Text = string.Empty;
			_parseTree = null;

			btnRun.Sensitive = _grammar is ICanRunSample;
			_language = new LanguageData (_grammar);
			_parser = new Parser (_language);
			ShowParserConstructionResults ();
			StartParserStatesHighlighter ();
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
				(_gridCompileErrors.Model as ListStore).AppendValues (null, ex.Message, null);
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
			}
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
			DefineCompilerErrorsHeader ();
			DefineParseTraceHeader ();
			ClearTreeView (tvParseTree);
			ClearTreeView (tvAST);
		}

		private void ShowLanguageInfo ()
		{
			if (_grammar != null) {
				var langAttr = LanguageAttribute.GetValue (_grammar.GetType ());
				if (langAttr != null) {
					lblLanguage.Text = langAttr.LanguageName;
					lblLanguageVersion.Text = langAttr.Version;
					lblLanguageDescr.Text = langAttr.Description;
					txtGrammarComments.Buffer.Text = _grammar.GrammarComments;
				}
			}
		}

		private void DefineParseTraceHeader() {
			if (_gridParserTrace.Model != null) {
				(_gridParserTrace.Model as ListStore).Clear ();
				(_gridParserTrace.Model as ListStore).AppendValues ( "State", "Stack Top", "Input", "Action" );
				_gridParserTrace.Orientation = Orientation.Vertical;
				_gridParserTrace.NRowHeaders = 0;
				_gridParserTrace.NColHeaders = 1;
			}
		}

		private void DefineCompilerErrorsHeader() {
			if (_gridCompileErrors.Model != null) {
				(_gridCompileErrors.Model as ListStore).Clear ();
				(_gridCompileErrors.Model as ListStore).AppendValues ( "L.C.", "Error Message", "Parser State" );
				_gridCompileErrors.Orientation = Orientation.Vertical;
				_gridCompileErrors.NRowHeaders = 0;
				_gridCompileErrors.NColHeaders = 1;
			}
		}

		private void ShowCompilerErrors ()
		{
			DefineCompilerErrorsHeader ();
			if (_parseTree == null || _parseTree.ParserMessages.Count == 0)
				return;
			foreach (var err in _parseTree.ParserMessages) {
				(_gridCompileErrors.Model as ListStore).AppendValues (err.Location.ToUiString (), err.Message, err.ParserState.ToString (), err);
			}
			var needPageSwitch = tabBottom.CurrentPage != 2 && //  pageParserOutput
			                     !(tabBottom.CurrentPage == 3 && chkParserTrace.Active);
			if (needPageSwitch)
				tabBottom.CurrentPage = 2; // pageParserOutput;
			_gridCompileErrors.ShowAll ();
		}

		private void ShowParseTrace ()
		{
			DefineParseTraceHeader ();
			String cellColor;
			foreach (ParserTraceEntry entry in _parser.Context.ParserTrace) {
				if (entry.IsError) {
					cellColor = "red";
				} else {
					cellColor = "black";
				}
				string cInput = "";
				if (entry.Input != null) {
					cInput = _regexCleanWhiteSpace.Replace (entry.Input.ToString (), @" ");
					cInput = System.Text.RegularExpressions.Regex.Replace(cInput.ToString(),@"\s+"," ");
				}
				string cStackTop = _regexCleanWhiteSpace.Replace (entry.StackTop.ToString (), @" ");
				cStackTop = System.Text.RegularExpressions.Regex.Replace(cStackTop.ToString(),@"\s+"," ");
				ListStore listmodel = (_gridParserTrace.Model as ListStore);
				// "State", "Stack Top", "Input", "Action"}
				listmodel.AppendValues(entry.State.Name, cStackTop, cInput, entry.Message);
			}
			//Show tokens
			String foo;
			foreach (Token tkn in _parseTree.Tokens) {
				if (chkExcludeComments.Active && tkn.Category == TokenCategory.Comment)
					continue;
				foo = tkn.ToString ();
				string cleanedup = System.Text.RegularExpressions.Regex.Replace (foo, @"\s+", " ");
				(lstTokens.Model as ListStore).AppendValues ( cleanedup, tkn );
			}
			_gridParserTrace.ShowAll ();
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
				// Store the Grammar items from the dll in the combobox ListStore model
				UpdateModelFromGrammarList (grammarlist, cboGrammars.Model as ListStore);
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

		private void StartParserStatesHighlighter ()
		{
			_IronyParserStatesSyntaxMode = new IronyParserStatesSyntaxMode (null);
			Mono.TextEditor.Highlighting.SyntaxModeService.InstallSyntaxMode ("text/x-irony-parserstates", new SyntaxModeProvider (doc => _IronyParserStatesSyntaxMode ));
			_txtParserStates.Document.MimeType = "text/x-irony-parserstates";
		}

		private void StopParserStatesHighlighter ()
		{
		}

		private void StartTestSourceHighlighter ()
		{
			_IronySyntaxMode = new IronySyntaxMode (null);
			Mono.TextEditor.Highlighting.SyntaxModeService.InstallSyntaxMode ("text/x-irony", new SyntaxModeProvider (doc => _IronySyntaxMode ));

			var termList = _language.GrammarData.Terminals.ToList();
			var tStringList = new List<string>();
			foreach (var t in termList) {
				if (t.GetType().GetProperty("Text") != null) {
					tStringList.Add ( t.ToString() );
				}
			}
			_IronySyntaxMode.AddTerminals (tStringList);

			var ntList = _language.GrammarData.NonTerminals.ToList();
			var ntStringList = new List<string>();
			foreach (var nt in ntList) {
				if (nt.GetType().GetProperty("Text") != null) {
					ntStringList.Add ( nt.ToString() );
				}
			}
			_IronySyntaxMode.AddNonTerminals (ntStringList);
			_teEditor.Document.MimeType = "text/x-irony";
			var tmp = _teEditor.Document.Text;
			_teEditor.Document.Text = String.Empty;
			_teEditor.Document.Text = tmp;
		}

		private void StopTestSourceHighlighter ()
		{
			_teEditor.Document.MimeType = "text/stophighlighting";
			var tmp = _teEditor.Document.Text;
			_teEditor.Document.Text = String.Empty;
			_teEditor.Document.Text = tmp;
		}

		private void EnableHighlighter (bool enable)
		{
			if (enable)
				StartTestSourceHighlighter ();
			else
				StopTestSourceHighlighter();
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

		protected void OnFcbtnFileOpenSelectionChanged (object sender, EventArgs e)
		{
			FileChooserButton file = sender as FileChooserButton;
			string location = file.Filename;
			if (!string.IsNullOrEmpty (location)) {
				ClearParserOutput ();
				LoadSourceFile (location);
			}
		}

		private void cboParseMethod_SelectedIndexChanged (object sender, EventArgs e)
		{
			//changing grammar causes setting of parse method combo, so to prevent double-call to ConstructParser
			// we don't do it here if _changingGrammar is set
			if (!_changingGrammar)
				CreateParser ();
		}

		protected void OnDefaultActivated (object sender, EventArgs e)
		{
			(sender as Gtk.Window).Present ();
		}

		protected void OnRealized (object sender, EventArgs e)
		{
			(sender as Gtk.Window).Present ();
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

		protected void OnStateChanged (object sender, EventArgs e)
		{
			_fullScreen = !_fullScreen;
		}

		protected void OnQuitActionActivated (object sender, EventArgs e)
		{
			fmExploreGrammarWindowClosing ();
			Application.Quit ();
		}

		protected void OnMinimizeActionActivated (object sender, EventArgs e)
		{
			this.Iconify ();
		}

		protected void OnCloseGrammarAssemblyActionActivated (object sender, EventArgs e)
		{
			RemoveAllGrammarsInList ();
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

		protected void OnBtnSearchClicked (object sender, EventArgs e)
		{
			DoSearch ();
		}

		protected void OnTxtSearchEditingDone (object sender, EventArgs e)
		{
			DoSearch ();
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
					TreeIter ti;
					btnManageGrammars.GetActiveIter (out ti);
					ListStore listStore = btnManageGrammars.Model as ListStore;
					(listStore.GetValue (ti, 1) as System.Action) ();
				} finally {
					btnManageGrammars.SetActiveIter (TreeIter.Zero);
					_InManageGrammarSelectiion = false;
				}
			}
		}

		void LocateTreeNodes (TextSegment segment)
		{
			if (segment != null) {
				if (!_TreeSelectionChanging) {
					LocateTreeNode (tvParseTree, segment.Offset, segment.Length);
					tvParseTree.CollapseAll ();
					tvParseTree.ExpandToPath (_currentPath);
					var treeselect = tvParseTree.Selection;
					tvParseTree.SetCursor (_currentPath, tvParseTree.Columns [0], false);
					LocateTreeNode (tvAST, segment.Offset, segment.Length);
					tvAST.CollapseAll ();
					tvAST.ExpandToPath (_currentPath);
					treeselect = tvParseTree.Selection;
					tvAST.SetCursor (_currentPath, tvAST.Columns [0], false);
				}
			}
		}

		protected void OnBtnLocateClicked (object sender, EventArgs e)
		{
			if (!_TreeSelectionChanging) {
				if (_parser != null) {
					if (_teEditor.IsSomethingSelected) {
						if (_parseTree == null) {
							ParseSample ();
						}
						LocateTreeNodes (_teEditor.SelectionRange);
					}
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

		bool _TreeSelectionChanging = false;
		protected void OnTvParseTreeRowActivated (object o, RowActivatedArgs args)
		{
			try {
				_TreeSelectionChanging = true;
				TreeIter ti;
				(tvParseTree.Model as TreeStore).GetIter (out ti, args.Path);
				ParseTreeNode parseNode = (tvParseTree.Model as TreeStore).GetValue (ti, 1) as ParseTreeNode;
				if (parseNode != null) {
					ShowSourcePosition (_teEditor, parseNode.Span);
				}
			} finally {
				_TreeSelectionChanging = false;
			}
		}

		protected void OnTvASTRowActivated (object o, RowActivatedArgs args)
		{
			try {
				_TreeSelectionChanging = true;
				TreeIter ti;
				(tvAST.Model as TreeStore).GetIter (out ti, args.Path);
				Irony.Interpreter.Ast.AstNode astNode = (tvAST.Model as TreeStore).GetValue (ti, 1) as Irony.Interpreter.Ast.AstNode;
				if (astNode != null) {
					ShowSourcePosition (_teEditor, astNode.Span);
				}
			} finally {
				_TreeSelectionChanging = false;
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

		private TreeIter LocateTreeNode (TreeView tv, int start, int end)
		{
			_currentNode = TreeIter.Zero;
			_start = start;
			_end = end;
			tv.Model.Foreach (new TreeModelForeachFunc (ForEachTreeNode));
			return _currentNode;
		}

		private void OnParseStateCellClicked (object sender, CellPressEventArgs args)
		{
			int _row = args.Row;
			if (_row >= _parser.Context.ParserTrace.Count) {
				_row = 0;
			}
			var entry = _parser.Context.ParserTrace[_row];
			switch (args.Column) {
			case 0: //state
			case 3: //action
				LocateParserState(entry.State);
				break;
			case 1: //stack top
				if (entry.StackTop != null) {
					ShowSourcePosition (_teEditor, entry.StackTop.Span);
					LocateTreeNodes (_teEditor.SelectionRange);
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
					LocateTreeNodes (_teEditor.SelectionRange);
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

		private void OnGridCompileErrorsClick(object sender, CellPressEventArgs args) {
			TreeIter ti;
			var treemodel = _gridCompileErrors.Model as ListStore;
			treemodel.GetIter (out ti, args.Path );
			if (!ti.Equals (null)) {
				var err = treemodel.GetValue (ti, 3) as LogMessage;
				if (!err.Equals(null)) {
					switch (args.Column) {
					case 0: //state
					case 1: //stack top
						ShowSourcePosition(_teEditor, err.Location.Line, err.Location.Column, 1);
						break;
					case 2: //input
						if (err.ParserState != null)
							LocateParserState(err.ParserState);
						break;
					}
				}
			}
		}
	}
}



