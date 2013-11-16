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
#endregion
using Gtk;
using System;

namespace Irony.GrammerExplorer
{
	public partial class dlgShowException : Gtk.Dialog
	{
		public dlgShowException (string error)
		{
			this.Build ();
			this.Hide ();
			this.SetPosition (WindowPosition.CenterOnParent);
			txtException.Buffer.Text = error;
			this.Show ();
		}
	}
}

