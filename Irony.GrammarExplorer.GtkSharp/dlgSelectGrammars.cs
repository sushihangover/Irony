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
using System.Data;
using System.Linq;
using System.Reflection;
using Irony.Parsing;
using Irony.GrammarExplorer;

namespace Irony.GrammarExplorer
{
	public partial class dlgSelectGrammars : Gtk.Dialog
	{
		public dlgSelectGrammars ()
		{
		}

		private void OnGrammarToggled(object sender, ToggledArgs args) {
			TreeIter iter;
			ListStore listStore = lstGrammars.Model as ListStore;
			if (listStore.GetIter (out iter, new TreePath(args.Path))) 
			{
				bool old = (bool) listStore.GetValue(iter,0);
				listStore.SetValue(iter, 0, !old);
			}
		}

		private void SetupModel_LstGrammars ()
		{
			ListStore modelLstGrammars = new ListStore (typeof(bool), typeof(string), typeof(GrammarItem));
			lstGrammars.Model = modelLstGrammars;
			lstGrammars.HeadersVisible = false;
			CellRendererToggle toggleGrammar = new CellRendererToggle();
			toggleGrammar.Activatable = true;
			toggleGrammar.Toggled += OnGrammarToggled;
			lstGrammars.AppendColumn ("CheckBox", toggleGrammar, "active", 0);
			lstGrammars.AppendColumn ("Grammar Caption", new CellRendererText (), "text", 1);
		}

		private  bool ContainsGrammar(GrammarItemList items, GrammarItem item)
		{
			foreach (var listItem in items)
				if (listItem.TypeName == item.TypeName && listItem.Location == item.Location)
					return true;
			return false;
		}

		public delegate void ProcessGrammars(GrammarItemList grammerlist);
		ProcessGrammars _GrammarItemListConsume;
		public void ShowGrammars(string assemblyPath, GrammarItemList loadedGrammars, ProcessGrammars callbackparent, Gtk.Window parent = null) 
		{
			_GrammarItemListConsume = callbackparent;
			var fromGrammars = LoadGrammars(assemblyPath);
			if (fromGrammars != null) {
				this.Build ();
				// Begin: hack; due to 'Build' always calling Run() in the autogen'd code in MonoDevelop & w/ no parent
				this.Visible = false;
				this.Parent = parent;
				this.SetPosition (WindowPosition.CenterOnParent);
				this.Visible = true;
				// End: hack
				SetupModel_LstGrammars ();
				(lstGrammars.Model as ListStore).Clear ();
				foreach(GrammarItem item in fromGrammars) {
					(lstGrammars.Model as ListStore).AppendValues (true, item.Caption, item);
				}
			}
		}

		private GrammarItemList LoadGrammars(string assemblyPath) 
		{
			Assembly asm = null;
			try {
				asm = GrammarLoader.LoadAssembly(assemblyPath);
			} catch (Exception ex) {
				MessageDialog md = new MessageDialog(this, 
				                                     DialogFlags.DestroyWithParent, MessageType.Warning, 
				                                     ButtonsType.Close, "Failed to load assembly: " + ex.Message);
				md.Run();
				md.Destroy();
				return null;
			}
			var types = asm.GetTypes();
			var grammars = new GrammarItemList();
			foreach (Type t in types) {
				if (t.IsAbstract) continue;
				if (!t.IsSubclassOf(typeof(Grammar))) continue;
				grammars.Add(new GrammarItem(t, assemblyPath));
			}
			if (grammars.Count == 0) {
				MessageDialog md = new MessageDialog(this, 
				                                     DialogFlags.DestroyWithParent, MessageType.Warning, 
				                                     ButtonsType.Close, "No classes derived from Irony.Grammar were found in the assembly. ");
				md.Run();
				md.Destroy();
				return null;
			}
			return grammars;
		}

		GrammarItemList _result = new GrammarItemList();
		private bool ForEachSelectedGrammar(TreeModel model, TreePath path, TreeIter iter) 
		{
			ListStore listStore = model as ListStore;
			bool _checked = (bool) listStore.GetValue (iter, 0);
			if (_checked) {
				var item = listStore.GetValue (iter, 2) as GrammarItem;
				item._loading = false;
				_result.Add(item);
			}
			return false; 
		} 

		protected void OnButtonOkClicked (object sender, EventArgs e)
		{
			ListStore listStore = lstGrammars.Model as ListStore;
			listStore.Foreach(new TreeModelForeachFunc(ForEachSelectedGrammar));
			Gtk.Application.Invoke (delegate {
				_GrammarItemListConsume(_result);
			});
			this.Hide ();
		}

		protected void OnButtonCancelClicked (object sender, EventArgs e)
		{
			this.Destroy ();
		}

		bool _toggleState; 
		private bool ForEachToggleGrammar(TreeModel model, TreePath path, TreeIter iter) 
		{
			ListStore listStore = model as ListStore;
			listStore.SetValue (iter, 0, _toggleState);
			return false; 
		} 

		protected void OnBtnUncheckAllClicked (object sender, EventArgs e)
		{
			_toggleState = false;
			ListStore listStore = lstGrammars.Model as ListStore;
			listStore.Foreach(new TreeModelForeachFunc(ForEachToggleGrammar));
		}

		protected void OnBtnCheckAllClicked (object sender, EventArgs e)
		{
			_toggleState = true;
			ListStore listStore = lstGrammars.Model as ListStore;
			listStore.Foreach(new TreeModelForeachFunc(ForEachToggleGrammar));
		}
	}
}

