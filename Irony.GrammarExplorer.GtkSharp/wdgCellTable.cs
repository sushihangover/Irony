using System;
using System.Text;
using Gtk;
using GLib;
using Gdk;
using System.Runtime.InteropServices;

namespace Irony.GrammerExplorer
{
	public class CellData
	{
		public CellData (string Txt)
		{
			Text = Txt;
			Data = null;
			Error = false;
		}
		public string Text;
		public object Data;
		public Gtk.Justification Justify = Gtk.Justification.Left;
		public bool Error;
	}

	[StructLayout(LayoutKind.Sequential)]
	public class CellRowColumn
	{
		int _row;
		int _column;
		object _data;
		public CellRowColumn (int Row, int Column, object Data = null)
		{
			_row = Row;
			_column = Column;
			_data = Data;
		}
		public int RowIndex {
			get {
				return _row;
			}
		}
		public int ColumnIndex {
			get {
				return _column;
			}
		}
		public object Data {
			get {
				return _data;
			}
		}
	}

	[System.ComponentModel.ToolboxItem (true)]
	public partial class wdgCellTable : Gtk.Bin
	{
		public wdgCellTable (uint Columns)
		{
			_rows = 0;
			_columns = Columns - 1;
			this.Build ();
			header = new Gtk.Table (1, _columns + 1, true);
			header.Visible = false;
			header.HeightRequest = 0;
			vbox2.PackStart (header, false, false, 0);

			hsep = new HSeparator ();
			hsep.Visible = false;
			hsep.HeightRequest = 0;
			vbox2.PackStart (hsep, false, false, 0);

			buttonTable = new Table (1, Columns, true);
			buttonTable.ColumnSpacing = 0;
			buttonTable.RowSpacing = 5;
			buttonTable.BorderWidth = 0;
			sw = new ScrolledWindow ();
			sw.AddWithViewport (buttonTable);
			vbox2.PackEnd (sw, true, true, 0);
			this.ShowAll ();
		}

		private uint _columns;
		private uint _rows;
		private Table buttonTable;
		private Table header;
		private HSeparator hsep;
		private ScrolledWindow sw;

		public uint NRows {
			get {
				return _rows;
			}
		}

		public uint NColumns {
			get {
				return _columns;
			}
		}

		public bool HeaderVisible {
			get {
				return header.Visible;
			}
			set {
				header.Visible = value;
				hsep.Visible = value;
			}
		}

		public event ButtonReleaseEventHandler GridCellClicked;
		public event EventHandler GridCellHover;

		public void DefineHeader(params String[] cols)
		{
			foreach (Widget col in header.AllChildren) {
				col.Destroy ();
			}
			header.NRows = 1;
			header.NColumns = (uint) cols.Length;
			header.HeightRequest = 20;
			hsep.HeightRequest = 2;
			for (uint column = 0; column < _columns + 1; column++) {
				Label lbl = new Label ();
				lbl.Text = cols [column];
				lbl.Name = "C" + (column + 1).ToString () + "R" + (_rows + 1).ToString ();
				header.Attach (lbl, column, column + 1, 1, 2);
			}
			header.Visible = true;
			hsep.Visible = true;
//			Gdk.Color color = new Gdk.Color();
//			Gdk.Color.Parse("red", ref color);
//			hsep.ModifyBg(StateType.Normal, color);
			ShowAll ();

//			this.WidgetEvent += delegate(object o, WidgetEventArgs args) {
//				Console.WriteLine (args.Event.Type);
//			};
		}

		private void DeleteWidget(Widget wgt)
		{
			try {
				buttonTable.Remove (wgt);
				wgt.Destroy ();
			} catch {
			}
		}

		public void ClearAll()
		{
			_rows = 0;
			sw.Remove (buttonTable);
			buttonTable.Foreach (DeleteWidget);
			buttonTable.Destroy ();
			buttonTable = null;
			System.GC.Collect ();
			buttonTable = new Table (1, _columns, true);
			buttonTable.ColumnSpacing = 0;
			buttonTable.RowSpacing = 5;
			buttonTable.BorderWidth = 0;
			sw.AddWithViewport (buttonTable);
			sw.ShowAll ();
			this.ExposeEvent += (object o, ExposeEventArgs args) => {
				buttonTable.CheckResize();
			};
		}

		public void AddRow (params CellData[] cells)
		{
			buttonTable.NRows++;
			for (uint column = 0; column < cells.Length; column++) 
			{
				EventBox eb = new EventBox ();
				Label lbl = new Label (cells [column].Text);
				lbl.Justify = cells [column].Justify;
				lbl.UseMarkup = true;
				lbl.AppPaintable = true;
				lbl.Name = "" + (column + 1).ToString () + "_" + (_rows + 1).ToString ();
				eb.Add (lbl);
				if (cells[column].Data != null) 
				{
					GCHandle handle = GCHandle.Alloc(cells[column].Data);
					IntPtr dataPtr = (IntPtr) handle;
					#pragma warning disable 612, 618
					lbl.SetData (lbl.Name, dataPtr);
					#pragma warning restore 612, 618
				}

				//eb.WidgetEvent += delegate(object o, WidgetEventArgs args) {
				//	Console.WriteLine (args.Event.Type);
				//};

				eb.EnterNotifyEvent += (object o, EnterNotifyEventArgs args) => 
				{
					Label tmpLbl = (((o as EventBox).Child) as Label);
					tmpLbl.Markup = string.Format ("<span background=\"black\" foreground=\"white\">{0}</span>", tmpLbl.Text);
					if ( GridCellHover != null ) 
					{
						GridCellHover(tmpLbl, args);
					}
				};

				eb.LeaveNotifyEvent += (object o, LeaveNotifyEventArgs args) => 
				{
					Label tmpLbl = (((o as EventBox).Child) as Label);
					tmpLbl.Markup = string.Format ("<span>{0}</span>", tmpLbl.Text);
				};

				eb.ButtonReleaseEvent += (object o, ButtonReleaseEventArgs args) => 
				{
					Label tmpLbl = (((o as EventBox).Child) as Label);
					if ( GridCellClicked != null ) {
//						var rowcolumn = new CellRowColumn( Convert.ToInt32(tmpLbl.Name.ToString().Split('_')[1]) - 1, Convert.ToInt32(tmpLbl.Name.ToString().Split('_')[0]) - 1);
//						args.Args = new object [] { (object) rowcolumn };
						GridCellClicked(o, args);
					}
				};
				buttonTable.Attach (eb, column, column + 1, _rows, _rows + 1);
			}
			buttonTable.Homogeneous = false;
			ShowAll ();
			_rows++;
		}
	}
}

