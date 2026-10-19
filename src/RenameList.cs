// The middle of the RENAME tab: the file list and the "give it a name" window.
//
// The list is virtual: Windows keeps no rows, it only asks for the ones on screen, and every row / tile is drawn here
// (tick box, thumbnail, names, colours). A folder with thousands of pictures opens at once. (The first version kept every
// row with its own colours and fonts: 493 pictures froze the window for ~40 s.)
// Thumbnails are made in the background and shown a few times a second, a batch at a time.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

/* the thumbnails use the WPF image decoder, and WPF, when it first loads, switches the whole process to another screen
   scaling mode: on a 125% screen the window jumped to another size in the RENAME tab and texts were cut. This keeps
   the scaling the window started with. */
[assembly: System.Windows.Media.DisableDpiAwareness]

namespace CuWatermark
{
	partial class MainForm
	{
		[DllImport( "user32.dll", EntryPoint = "SendMessage" )]
		static extern IntPtr SendMsg( IntPtr h, int msg, IntPtr w, IntPtr l );

		static readonly Color ROWALT = Color.FromArgb( 19, 24, 35 ), ROWSEL = Color.FromArgb( 82, 40, 78 );
		const int ROW_H = 34;

		List<Entry> rows = new List<Entry>(); // what the list shows: searched and sorted "entries"
		readonly Dictionary<string, Bitmap> thumbSmall = new Dictionary<string, Bitmap>( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, Bitmap> thumbBig = new Dictionary<string, Bitmap>( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, Bitmap[]> extIcon = new Dictionary<string, Bitmap[]>( StringComparer.OrdinalIgnoreCase );
		readonly Queue<KeyValuePair<string, Bitmap[]>> ready = new Queue<KeyValuePair<string, Bitmap[]>>(); // made, not shown yet
		Bitmap folderSmall, folderBig;
		System.Windows.Forms.Timer uiTick; // a few times a second: new thumbnails in, loading bars up to date
		bool downOnTick; // the last click was on a tick box (so a double-click there does not open the name window)
		/* loading */
		Panel loadStrip;
		Label loadLabel;
		SlimBar loadBar, actBar;
		int readGen, thumbTotal, thumbDone, loadHideAt, actHideAt;
		bool reading, copying;
		Func<Entry, bool> afterRead; // ticks to set once the folder is read (files dropped on the window)

		/* ---------- building ---------- */
		void BuildRenameCenter()
		{
			folderSmall = FolderIcon( TH_SMALL );
			folderBig = FolderIcon( TH_BIG );

			renWrap = new Panel();
			renWrap.Dock = DockStyle.Fill;
			renWrap.BackColor = BG;
			renWrap.Visible = false;

			renBar = new Panel();
			renBar.Dock = DockStyle.Top;
			renBar.Height = 92;
			renBar.BackColor = BG;
			bUp = MakeButton( renBar, "↑", 0, 6, 38, 34, false );
			bUp.Click += delegate { string p = Path.GetDirectoryName( renFolder ); if ( p != null ) OpenFolder( p ); };
			renPath = MakeLabel( renBar, "", 46, 2, Color.White, 12.5f, true ); // the folder's name, big
			renPath.AutoSize = false;
			renPath.AutoEllipsis = true;
			renPath.Height = 26;
			renParent = MakeLabel( renBar, "", 48, 27, MUTED, 8.25f, false ); // where it is, small
			renParent.AutoSize = false;
			renParent.AutoEllipsis = true;
			renParent.Height = 16;
			( (RoundButton) bUp ).Glyph = Look.Up;
			bUp.Text = "";
			bViewList = MakeButton( renBar, "List", 0, 6, 92, 34, false );
			bViewThumbs = MakeButton( renBar, "Pictures", 0, 6, 112, 34, false );
			( (RoundButton) bViewList ).Glyph = Look.List;
			( (RoundButton) bViewThumbs ).Glyph = Look.Grid;
			bViewList.Click += delegate { SetRenView( "list" ); };
			bViewThumbs.Click += delegate { SetRenView( "thumbs" ); };

			tSearch = new TextBox();
			tSearch.Location = new Point( 0, 52 );
			tSearch.Width = 200;
			tSearch.BackColor = FIELD;
			tSearch.ForeColor = TEXT;
			tSearch.BorderStyle = BorderStyle.FixedSingle;
			tSearch.Font = new Font( "Segoe UI", 10.5f );
			tSearch.TextChanged += delegate { RebuildItems(); };
			renBar.Controls.Add( tSearch );
			Cue( tSearch, "🔍  Search in this folder" );
			Label lShow = MakeLabel( renBar, "Show", 210, 57, MUTED, 9f, false );
			cShow = MakeCombo( renBar, 250, 52, 110, Renamer.Shows.Cast<object>().ToArray(), S.RShow );
			cShow.SelectedIndexChanged += delegate { if ( !renFilling ) ReloadFolder(); };
			kSub = MakeCheck( renBar, "Subfolders too", 372, 55, S.RSub );
			kSub.CheckedChanged += delegate { if ( !renFilling ) ReloadFolder(); };
			renBar.Resize += delegate
			{
				lShow.Left = tSearch.Right + 12; // one after the other, whatever the Windows scaling
				lShow.Top = tSearch.Top + ( tSearch.Height - lShow.Height ) / 2;
				cShow.Left = lShow.Right + 6;
				cShow.Top = tSearch.Top + ( tSearch.Height - cShow.Height ) / 2;
				kSub.Left = cShow.Right + 14;
				kSub.Top = tSearch.Top + ( tSearch.Height - kSub.Height ) / 2;
				int w = renBar.Width;
				bViewThumbs.Left = w - bViewThumbs.Width;
				bViewList.Left = bViewThumbs.Left - bViewList.Width - 6;
				renPath.Width = renParent.Width = Math.Max( 50, bViewList.Left - 56 );
			};

			renView = new ListView();
			renView.Dock = DockStyle.Fill;
			renView.VirtualMode = true;
			renView.OwnerDraw = true;
			renView.View = View.Details;
			renView.FullRowSelect = true;
			renView.HideSelection = false;
			renView.ShowItemToolTips = true;
			renView.BackColor = FIELD;
			renView.ForeColor = TEXT;
			renView.BorderStyle = BorderStyle.None;
			renView.Font = new Font( "Segoe UI", 9.75f );
			renView.SmallImageList = Sizer( 1, ROW_H );    // empty images: they only give the rows their height
			renView.LargeImageList = Sizer( TH_BIG, TH_BIG ); // and the tiles their size
			renView.Columns.Add( "Name", 240 );
			renView.Columns.Add( "New name", 240 );
			renView.Columns.Add( "Type", 54 );
			renView.Columns.Add( "Size", 70, HorizontalAlignment.Right );
			renView.Columns.Add( "Modified", 140 );
			renView.Columns.Add( "", 150 );
			typeof( Control ).GetProperty( "DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).SetValue( renView, true, null ); // no flicker

			renView.RetrieveVirtualItem += delegate( object o, RetrieveVirtualItemEventArgs e )
			{
				Entry en = e.ItemIndex < rows.Count ? rows[e.ItemIndex] : null;
				ListViewItem it = new ListViewItem( en == null ? "" : en.Rel, 0 );
				for ( int c = 1; c < 6; c++ ) it.SubItems.Add( "" );
				if ( en != null ) it.ToolTipText = en.Rel + ( en.State == 1 || en.State == 3 ? "\n→  " + en.NewName : "" ) + ( en.State == 3 ? "\n" + en.Note : "" );
				e.Item = it;
			};
			renView.DrawColumnHeader += delegate( object o, DrawListViewColumnHeaderEventArgs e )
			{
				using ( Brush hb = new SolidBrush( HEAD ) ) e.Graphics.FillRectangle( hb, e.Bounds );
				string t = e.Header.Text + ( e.ColumnIndex == sortCol && e.ColumnIndex != 1 && e.ColumnIndex != 5 ? ( sortDesc ? "  ▼" : "  ▲" ) : "" );
				using ( Font hf = new Font( "Segoe UI", 9f, FontStyle.Bold ) )
					TextRenderer.DrawText( e.Graphics, t, hf, new Rectangle( e.Bounds.X + ( e.ColumnIndex == 0 ? 66 : 6 ), e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height ), e.ColumnIndex == 1 ? GREEN : MUTED,
						TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | ( e.ColumnIndex == 3 ? TextFormatFlags.Right : TextFormatFlags.Left ) );
			};
			renView.DrawItem += delegate( object o, DrawListViewItemEventArgs e )
			{
				if ( e.ItemIndex >= rows.Count || renView.View == View.Details ) return; // a row: its cells draw themselves
				DrawTile( e.Graphics, e.Bounds, rows[e.ItemIndex], Sel( e.ItemIndex ) );
			};
			renView.DrawSubItem += delegate( object o, DrawListViewSubItemEventArgs e )
			{
				if ( e.ItemIndex >= rows.Count || renView.View != View.Details ) return;
				DrawCell( e.Graphics, e.Bounds, e.ColumnIndex, e.ItemIndex );
			};

			renView.MouseDown += delegate( object o, MouseEventArgs e )
			{
				downOnTick = false;
				if ( e.Button != MouseButtons.Left ) return;
				ListViewHitTestInfo h = renView.HitTest( e.Location );
				if ( h.Item == null || !TickRect( h.Item.Index ).Contains( e.Location ) ) return;
				downOnTick = true;
				Toggle( new int[] { h.Item.Index } );
			};
			renView.DoubleClick += delegate
			{
				if ( downOnTick || renView.SelectedIndices.Count == 0 ) return;
				Open( renView.SelectedIndices[0] );
			};
			renView.KeyDown += delegate( object o, KeyEventArgs e )
			{
				if ( e.KeyCode == Keys.A && e.Control )
				{
					renView.BeginUpdate();
					for ( int i = 0; i < rows.Count; i++ ) renView.SelectedIndices.Add( i );
					renView.EndUpdate();
					e.Handled = true;
					return;
				}
				if ( e.KeyCode == Keys.Back ) { bUp.PerformClick(); e.Handled = true; return; }
				if ( renView.SelectedIndices.Count == 0 ) return;
				int first = renView.SelectedIndices[0];
				if ( e.KeyCode == Keys.Space ) { Toggle( renView.SelectedIndices.Cast<int>().ToArray() ); e.Handled = e.SuppressKeyPress = true; }
				else if ( e.KeyCode == Keys.F2 ) { EditName( first ); e.Handled = true; }
				else if ( e.KeyCode == Keys.Enter ) { Open( first ); e.Handled = e.SuppressKeyPress = true; }
			};
			renView.ColumnClick += delegate( object o, ColumnClickEventArgs e )
			{
				if ( e.Column == 1 || e.Column == 5 ) return;
				sortDesc = e.Column == sortCol ? !sortDesc : false;
				sortCol = e.Column;
				RebuildItems();
			};
			renView.Resize += delegate { FitRenColumns(); };
			renView.HandleCreated += delegate { TileSpacing(); };
			renView.AllowDrop = true;
			renView.DragEnter += OnDragEnter;
			renView.DragDrop += OnDragDrop;

			ContextMenuStrip menu = new ContextMenuStrip();
			menu.Items.Add( "✎  Give it a name…   (F2)", null, delegate { if ( renView.SelectedIndices.Count > 0 ) EditName( renView.SelectedIndices[0] ); } );
			menu.Items.Add( "Use the pattern for it again", null, delegate { foreach ( int i in renView.SelectedIndices ) customs.Remove( rows[i].Path ); QueueRedraw(); } );
			menu.Items.Add( new ToolStripSeparator() );
			menu.Items.Add( "✔  Tick the selected ones", null, delegate { SetTicks( renView.SelectedIndices.Cast<int>(), true ); } );
			menu.Items.Add( "Untick the selected ones", null, delegate { SetTicks( renView.SelectedIndices.Cast<int>(), false ); } );
			menu.Items.Add( new ToolStripSeparator() );
			menu.Items.Add( "Open", null, delegate { if ( renView.SelectedIndices.Count > 0 ) { Entry en = rows[renView.SelectedIndices[0]]; if ( en.IsDir ) OpenFolder( en.Path ); else try { System.Diagnostics.Process.Start( en.Path ); } catch { } } } );
			menu.Items.Add( "Show in Explorer", null, delegate { if ( renView.SelectedIndices.Count > 0 ) System.Diagnostics.Process.Start( "explorer.exe", "/select,\"" + rows[renView.SelectedIndices[0]].Path + "\"" ); } );
			renView.ContextMenuStrip = menu;

			/* the loading strip under the tools: "Reading the folder…", then "Thumbnails 123 / 493" */
			loadStrip = new Panel();
			loadStrip.Dock = DockStyle.Top;
			loadStrip.Height = 30;
			loadStrip.BackColor = BG;
			loadStrip.Visible = false;
			loadLabel = MakeLabel( loadStrip, "", 0, 2, MUTED, 8.75f, false );
			loadBar = new SlimBar();
			loadBar.Location = new Point( 0, 22 );
			loadStrip.Controls.Add( loadBar );
			loadStrip.Resize += delegate { loadBar.Width = loadStrip.Width; };

			renWrap.Controls.Add( renView );
			renWrap.Controls.Add( loadStrip );
			renWrap.Controls.Add( renBar );
			center.Controls.Add( renWrap );
			renWrap.BringToFront();
			SetRenView( S.RView );

			uiTick = new System.Windows.Forms.Timer();
			uiTick.Interval = 150;
			uiTick.Tick += delegate { ShowReadyThumbs(); Progress(); };
			uiTick.Start();
		}

		void ShowLoading( string text, bool busy )
		{
			loadLabel.Text = text;
			if ( busy ) { loadBar.Reset(); loadBar.Busy = true; }
			loadStrip.Visible = true;
			loadHideAt = 0;
		}

		/* the bars follow the work; a finished bar stays full for a moment, then goes */
		void Progress()
		{
			int now = Environment.TickCount;
			if ( !reading && thumbTotal > 0 && loadStrip.Visible && loadHideAt == 0 )
			{
				int done = Math.Min( thumbDone, thumbTotal );
				loadBar.Busy = false;
				loadBar.Maximum = thumbTotal;
				loadBar.Value = done;
				loadLabel.Text = done < thumbTotal ? "Thumbnails   " + done + " / " + thumbTotal : "✔  " + thumbTotal + " thumbnails ready";
				if ( done >= thumbTotal ) loadHideAt = now + 900;
			}
			if ( loadHideAt != 0 && now - loadHideAt >= 0 ) { loadStrip.Visible = false; loadHideAt = 0; }
			if ( actHideAt != 0 && now - actHideAt >= 0 ) { actBar.Visible = false; actHideAt = 0; }
		}

		/* the bar under RENAME fills up and goes */
		void ActDone()
		{
			actBar.Reset();
			actBar.Maximum = 1;
			actBar.Visible = true;
			actBar.Value = 1;
			actHideAt = Environment.TickCount + 1500;
		}

		static ImageList Sizer( int w, int h )
		{
			ImageList l = new ImageList();
			l.ColorDepth = ColorDepth.Depth32Bit;
			l.ImageSize = new Size( w, h );
			l.Images.Add( new Bitmap( w, h, PixelFormat.Format32bppArgb ) );
			return l;
		}

		void Cue( TextBox t, string text )
		{
			t.HandleCreated += delegate { SendMessage( t.Handle, 0x1501, (IntPtr) 1, text ); }; // EM_SETCUEBANNER: grey hint while empty
		}

		/* tiles of 156 x 184 in the picture view (LVM_SETICONSPACING) */
		void TileSpacing()
		{
			if ( renView.IsHandleCreated ) SendMsg( renView.Handle, 0x1035, IntPtr.Zero, (IntPtr) ( ( 184 << 16 ) | 156 ) );
		}

		void FitRenColumns()
		{
			if ( renView.View != View.Details ) return;
			float k = Dpi();
			int cT = (int) ( 56 * k ), cS = (int) ( 82 * k ), cM = (int) ( 150 * k ), cN = (int) ( 150 * k );
			int w = Math.Max( 300, renView.ClientSize.Width - cT - cS - cM - cN - 4 );
			renView.Columns[0].Width = w / 2;
			renView.Columns[1].Width = w - w / 2;
			renView.Columns[2].Width = cT;
			renView.Columns[3].Width = cS;
			renView.Columns[4].Width = cM;
			renView.Columns[5].Width = cN;
		}

		/* 1 at 100% Windows scaling, 1.5 at 150%… (fixed sizes in pixels grow with it) */
		float dpiK;
		float Dpi()
		{
			if ( dpiK == 0 ) using ( Graphics g = CreateGraphics() ) dpiK = Math.Max( 1f, g.DpiX / 96f );
			return dpiK;
		}

		void SetRenView( string v )
		{
			S.RView = v == "thumbs" ? "thumbs" : "list";
			renView.View = S.RView == "thumbs" ? View.LargeIcon : View.Details;
			TileSpacing();
			StyleToggle( bViewList, S.RView == "list" );
			StyleToggle( bViewThumbs, S.RView == "thumbs" );
			FitRenColumns();
			renView.Invalidate();
		}

		/* ---------- drawing ---------- */
		bool Sel( int i ) // LVM_GETITEMSTATE, LVIS_SELECTED: asks Windows directly, no list of the selection is built
		{
			return SendMsg( renView.Handle, 0x102C, (IntPtr) i, (IntPtr) 2 ) != IntPtr.Zero;
		}

		Color NameColor( Entry e )
		{
			return e.State == 3 ? RED : e.State == 1 ? GREEN : MUTED;
		}

		void DrawTick( Graphics g, Rectangle r, bool on )
		{
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using ( GraphicsPath p = Collage.Round( r, 4 ) )
			{
				if ( on )
				{
					using ( Brush b = new SolidBrush( PINK ) ) g.FillPath( b, p );
					using ( Pen w = new Pen( Color.White, 2f ) )
						g.DrawLines( w, new PointF[] { new PointF( r.X + r.Width * 0.24f, r.Y + r.Height * 0.52f ), new PointF( r.X + r.Width * 0.43f, r.Y + r.Height * 0.72f ), new PointF( r.X + r.Width * 0.78f, r.Y + r.Height * 0.3f ) } );
				}
				else
				{
					using ( Brush b = new SolidBrush( Color.FromArgb( 10, 13, 20 ) ) ) g.FillPath( b, p );
					using ( Pen e = new Pen( Color.FromArgb( 110, 120, 134 ), 1.5f ) ) g.DrawPath( e, p );
				}
			}
			g.SmoothingMode = SmoothingMode.None;
		}

		void DrawCell( Graphics g, Rectangle r, int col, int i )
		{
			Entry en = rows[i];
			bool sel = Sel( i );
			if ( col == 0 ) r.Width = renView.Columns[0].Width; // Windows gives the whole row for the first cell
			using ( Brush b = new SolidBrush( sel ? ROWSEL : i % 2 == 0 ? FIELD : ROWALT ) ) g.FillRectangle( b, r );
			TextFormatFlags tf = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
			Rectangle tr = new Rectangle( r.X + 6, r.Y, r.Width - 12, r.Height );
			switch ( col )
			{
				case 0:
					if ( sel ) using ( Brush b = new SolidBrush( PINK ) ) g.FillRectangle( b, r.X, r.Y + 4, 3, r.Height - 8 );
					DrawTick( g, new Rectangle( r.X + 10, r.Y + ( r.Height - 16 ) / 2, 16, 16 ), en.Checked );
					g.InterpolationMode = InterpolationMode.Bilinear;
					g.DrawImage( Pic( en, false ), r.X + 34, r.Y + ( r.Height - TH_SMALL ) / 2, TH_SMALL, TH_SMALL );
					TextRenderer.DrawText( g, en.Rel, renView.Font, new Rectangle( r.X + 68, r.Y, r.Width - 72, r.Height ), en.Checked ? TEXT : MUTED, tf );
					break;
				case 1: TextRenderer.DrawText( g, en.NewName, renView.Font, tr, NameColor( en ), tf ); break;
				case 2: TextRenderer.DrawText( g, en.IsDir ? "Folder" : Path.GetExtension( en.Path ).TrimStart( '.' ).ToUpperInvariant(), renView.Font, tr, MUTED, tf ); break;
				case 3: TextRenderer.DrawText( g, en.IsDir ? "" : Bytes( en.Size ), renView.Font, tr, MUTED, tf | TextFormatFlags.Right ); break;
				case 4: TextRenderer.DrawText( g, en.Mod == DateTime.MinValue ? "" : en.Mod.ToString( "dd MMM yyyy  HH:mm" ), renView.Font, tr, MUTED, tf ); break;
				default: TextRenderer.DrawText( g, en.Note, renView.Font, tr, en.State == 3 ? RED : en.Mine ? GREEN : MUTED, tf ); break;
			}
		}

		void DrawTile( Graphics g, Rectangle r, Entry en, bool sel )
		{
			using ( Brush b = new SolidBrush( sel ? ROWSEL : FIELD ) ) g.FillRectangle( b, r );
			if ( sel ) using ( Pen p = new Pen( PINK, 2f ) ) g.DrawRectangle( p, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3 );
			g.InterpolationMode = InterpolationMode.Bilinear;
			g.DrawImage( Pic( en, true ), r.X + ( r.Width - TH_BIG ) / 2, r.Y + 6, TH_BIG, TH_BIG );
			DrawTick( g, new Rectangle( r.X + 6, r.Y + 6, 20, 20 ), en.Checked );
			string label = en.State == 1 || en.State == 3 ? en.NewName : en.Rel;
			TextRenderer.DrawText( g, label, renView.Font, new Rectangle( r.X + 3, r.Y + TH_BIG + 10, r.Width - 6, Math.Max( 18, r.Bottom - r.Y - TH_BIG - 12 ) ),
				en.State == 0 ? MUTED : en.State == 2 ? TEXT : NameColor( en ), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix );
		}

		/* where the tick box of row / tile i is (a bit bigger than drawn: easier to hit) */
		Rectangle TickRect( int i )
		{
			Rectangle b = renView.GetItemRect( i );
			return renView.View == View.Details ? new Rectangle( b.X + 2, b.Y, 30, b.Height ) : new Rectangle( b.X + 2, b.Y + 2, 30, 30 );
		}

		/* ---------- ticks ---------- */
		void Toggle( int[] idx )
		{
			if ( idx.Length == 0 ) return;
			SetTicks( idx, !idx.All( i => rows[i].Checked ) );
		}

		void SetTicks( IEnumerable<int> idx, bool on )
		{
			foreach ( int i in idx ) if ( i < rows.Count ) rows[i].Checked = on;
			UpdateNames();
		}

		void TickAll( Func<Entry, bool> want )
		{
			foreach ( Entry e in entries ) e.Checked = want( e );
			UpdateNames();
		}

		/* the ticked items, in the order of the list (that is the order of the numbers) */
		List<Entry> Chosen()
		{
			return rows.Where( e => e.Checked ).ToList();
		}

		void Open( int i )
		{
			if ( i < 0 || i >= rows.Count ) return;
			if ( rows[i].IsDir ) OpenFolder( rows[i].Path );
			else EditName( i );
		}

		/* ---------- reading the folder ---------- */
		void ReloadFolder()
		{
			if ( renFolder == "" ) return;
			lock ( ready )
			{
				thumbGen++; // the thumbnail maker of the previous folder stops
				foreach ( KeyValuePair<string, Bitmap[]> t in ready ) { t.Value[0].Dispose(); t.Value[1].Dispose(); }
				ready.Clear();
			}
			foreach ( Bitmap b in thumbSmall.Values.Concat( thumbBig.Values ) ) b.Dispose();
			thumbSmall.Clear();
			thumbBig.Clear();
			thumbTotal = thumbDone = 0;
			entries.Clear();
			rows = new List<Entry>();
			renView.SelectedIndices.Clear();
			renView.VirtualListSize = 0;

			/* read in the background: a slow folder (Google Drive, thousands of files, subfolders) never freezes the window */
			string folder = renFolder;
			string show = cShow.SelectedItem == null ? "Everything" : cShow.SelectedItem.ToString();
			bool sub = kSub.Checked;
			int gen = ++readGen;
			reading = true;
			ShowLoading( "Reading  " + ( Path.GetFileName( folder ) == "" ? folder : Path.GetFileName( folder ) ) + "…", true );
			UpdateNames();
			Thread th = new Thread( delegate()
			{
				List<Entry> found = new List<Entry>();
				string error = null;
				try
				{
					if ( !sub && ( show == "Everything" || show == "Folders" ) )
						foreach ( string d in Directory.GetDirectories( folder ) )
							if ( !Hidden( d ) )
							{
								Entry e = new Entry();
								e.Path = d;
								e.Rel = Path.GetFileName( d );
								e.IsDir = true;
								try { e.Mod = Directory.GetLastWriteTime( d ); } catch { }
								found.Add( e );
							}
					if ( show != "Folders" )
					{
						List<string> fs = new List<string>();
						if ( sub ) Walk( folder, fs, 0 );
						else fs.AddRange( Directory.GetFiles( folder ) );
						foreach ( string f in fs )
						{
							if ( found.Count >= MAX_ITEMS || gen != readGen ) break;
							if ( Hidden( f ) || Path.GetFileName( f ).StartsWith( "~ren-" ) ) continue;
							if ( show == "Pictures" && !IsPic( f ) ) continue;
							Entry e = new Entry();
							e.Path = f;
							e.Rel = sub ? f.Substring( folder.TrimEnd( '\\' ).Length + 1 ) : Path.GetFileName( f );
							e.Checked = true; // files ticked, folders not: pictures are what is renamed most
							try { FileInfo fi = new FileInfo( f ); e.Size = fi.Length; e.Mod = fi.LastWriteTime; } catch { }
							found.Add( e );
						}
					}
				}
				catch ( Exception ex ) { error = ex.Message; }
				try
				{
					BeginInvoke( (MethodInvoker) delegate
					{
						if ( gen != readGen ) return; // another folder was opened meanwhile
						reading = false;
						entries.Clear();
						entries.AddRange( found );
						if ( afterRead != null ) { foreach ( Entry e in entries ) e.Checked = afterRead( e ); afterRead = null; }
						if ( found.Count >= MAX_ITEMS ) status3.Text = "Only the first " + MAX_ITEMS + " items are shown.";
						RebuildItems();
						if ( error != null ) { info.ForeColor = AMBER; info.Text = "Cannot open this folder: " + error; }
						StartThumbs();
						if ( thumbTotal == 0 ) loadHideAt = Environment.TickCount + 300; // nothing to make
					} );
				}
				catch { }
			} );
			th.IsBackground = true;
			th.Start();
		}

		static bool IsPic( string f )
		{
			string ext = Path.GetExtension( f ).ToLowerInvariant();
			return Marker.IsPicture( f ) || ext == ".webp" || ext == ".avif"; // webp: listed and renamed, without a thumbnail
		}

		Comparison<Entry> Sorter()
		{
			NaturalComparer nat = new NaturalComparer();
			int col = sortCol, dir = sortDesc ? -1 : 1;
			return delegate( Entry a, Entry b )
			{
				if ( a.IsDir != b.IsDir ) return a.IsDir ? -1 : 1; // folders first
				int r;
				switch ( col )
				{
					case 2: r = string.Compare( Path.GetExtension( a.Path ), Path.GetExtension( b.Path ), StringComparison.OrdinalIgnoreCase ); break;
					case 3: r = a.Size.CompareTo( b.Size ); break;
					case 4: r = a.Mod.CompareTo( b.Mod ); break;
					default: r = 0; break;
				}
				if ( r == 0 ) r = nat.Compare( a.Rel, b.Rel );
				return r * dir;
			};
		}

		static string Bytes( long b )
		{
			if ( b < 1024 ) return b + " B";
			if ( b < 1048576 ) return ( b / 1024.0 ).ToString( "0" ) + " KB";
			if ( b < 1073741824 ) return ( b / 1048576.0 ).ToString( "0.0" ) + " MB";
			return ( b / 1073741824.0 ).ToString( "0.00" ) + " GB";
		}

		/* search + sort -> rows; the list only learns how many there are */
		void RebuildItems()
		{
			if ( renFilling || renView == null ) return;
			List<Entry> show = entries.ToList();
			string q = tSearch.Text.Trim();
			if ( q != "" ) show = show.Where( e => e.Rel.IndexOf( q, StringComparison.OrdinalIgnoreCase ) >= 0 ).ToList();
			show.Sort( Sorter() );
			renView.SelectedIndices.Clear();
			rows = show;
			renView.VirtualListSize = rows.Count;
			if ( rows.Count > 0 ) renView.EnsureVisible( 0 ); // a new folder / search / order starts at the top
			UpdateNames();
		}

		void ReadRenameSettings()
		{
			if ( tPattern == null ) return;
			int w;
			S.RPattern = tPattern.Text;
			S.RStart = (int) nStart.Value;
			S.RStep = (int) nStep.Value;
			S.RDigits = cDigits.SelectedItem == null || !int.TryParse( cDigits.SelectedItem.ToString(), out w ) ? 0 : w;
			S.RFind = tFind.Text;
			S.RReplace = tRepl.Text;
			S.RCase = cCase.SelectedItem == null ? "As written" : cCase.SelectedItem.ToString();
			S.RSpaces = cSpaces.SelectedItem == null ? "Keep spaces" : cSpaces.SelectedItem.ToString();
			S.RExtLower = kExt.Checked;
			if ( cShow != null ) S.RShow = cShow.SelectedItem == null ? "Everything" : cShow.SelectedItem.ToString();
			if ( kSub != null ) S.RSub = kSub.Checked;
		}

		/* new names, colours and counts, every time something changes (only the rows on screen are redrawn) */
		void UpdateNames()
		{
			if ( renView == null || renFilling ) return;
			ReadRenameSettings();
			List<Entry> use = Chosen();
			renPlan = Renamer.Plan( use, S, customs );
			foreach ( Entry e in entries ) { e.State = 0; e.NewName = ""; e.Note = ""; e.Mine = false; }
			for ( int i = 0; i < use.Count; i++ )
			{
				Renamer.Step p = renPlan[i];
				Entry e = use[i];
				e.NewName = Path.GetFileName( p.Dst );
				e.Mine = p.Custom;
				e.State = p.Problem != null ? 3 : p.Same ? 2 : 1;
				e.Note = p.Problem != null ? "✖ " + p.Problem : p.Same ? "stays the same" : p.Custom ? "✎ your name" : "";
			}
			renView.Invalidate();

			int bad = renPlan.Count( p => p.Problem != null ), same = renPlan.Count( p => p.Same ), mine = renPlan.Count( p => p.Custom );
			int todo = renPlan.Count - same;
			lRenCount.Text = use.Count + " of " + rows.Count + " ticked" + ( mine > 0 ? "   ·   ✎ " + mine + " with your name" : "" );
			bool okOut = S.ROut != "" && Directory.Exists( S.ROut );
			StyleToggle( bSame, !S.RCopy );
			StyleToggle( bOther, S.RCopy );
			lRenOut.Text = okOut ? S.ROut : "No folder chosen yet — click to choose";
			lRenOut.ForeColor = okOut ? TEXT : AMBER;
			pWhere.Height = S.RCopy ? 178 : 98;
			string what = todo == 1 ? " ITEM" : " ITEMS";
			bRename.Text = use.Count == 0 ? "TICK WHAT TO RENAME" : bad > 0 ? "FIX THE RED NAMES FIRST" : todo == 0 ? "NOTHING TO CHANGE" : ( S.RCopy ? "COPY " : "RENAME " ) + todo + what;
			bRename.BackColor = use.Count > 0 && bad == 0 && todo > 0 ? PINK : BTN;
			info.ForeColor = bad > 0 ? RED : MUTED;
			info.Text = reading ? "Reading the folder…"
				: rows.Count == 0 ? ( entries.Count == 0 ? "This folder is empty — pick another one on the left" : "Nothing matches the search" )
				: use.Count == 0 ? "Tick the boxes of what you want to rename"
				: ( S.RCopy ? todo + " to copy" + ( okOut ? " to " + S.ROut : "" ) : todo + " to rename   ·   " + same + " stay the same" )
					+ ( bad > 0 ? "   ·   ✖ " + bad + " with a problem (in red)" : "   ·   ✔ no problems" );
		}

		/* ---------- pictures ---------- */
		/* the thumbnail of a picture when it is made, else the icon of its kind of file */
		Bitmap Pic( Entry e, bool big )
		{
			if ( e.IsDir ) return big ? folderBig : folderSmall;
			Bitmap b;
			if ( ( big ? thumbBig : thumbSmall ).TryGetValue( e.Path, out b ) ) return b;
			string ext = Path.GetExtension( e.Path ).ToLowerInvariant();
			Bitmap[] ic;
			if ( !extIcon.TryGetValue( ext, out ic ) )
			{
				try
				{
					using ( Icon i = System.Drawing.Icon.ExtractAssociatedIcon( e.Path ) )
					using ( Bitmap bb = i.ToBitmap() )
						ic = new Bitmap[] { Fit( bb, TH_SMALL, 1f ), Fit( bb, TH_BIG, 0.5f ) };
				}
				catch { ic = new Bitmap[] { folderSmall, folderBig }; }
				extIcon[ext] = ic;
			}
			return big ? ic[1] : ic[0];
		}

		/* a picture in the middle of a transparent square, as big as "part" of it allows */
		static Bitmap Fit( Image img, int size, float part )
		{
			Bitmap c = new Bitmap( size, size, PixelFormat.Format32bppArgb );
			float k = Math.Min( size * part / img.Width, size * part / img.Height );
			float w = img.Width * k, h = img.Height * k;
			using ( Graphics g = Graphics.FromImage( c ) )
			{
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = PixelOffsetMode.HighQuality;
				g.DrawImage( img, ( size - w ) / 2, ( size - h ) / 2, w, h );
			}
			return c;
		}

		static Bitmap FolderIcon( int size )
		{
			Bitmap b = new Bitmap( size, size, PixelFormat.Format32bppArgb );
			using ( Graphics g = Graphics.FromImage( b ) )
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				float m = size * ( size > 40 ? 0.2f : 0.08f ), w = size - 2 * m, h = w * 0.78f, y = ( size - h ) / 2;
				using ( Brush back = new SolidBrush( Color.FromArgb( 214, 150, 30 ) ) )
				using ( Brush front = new SolidBrush( Color.FromArgb( 255, 196, 72 ) ) )
				using ( GraphicsPath tab = Collage.Round( new RectangleF( m, y, w * 0.45f, h * 0.3f ), size * 0.04f ) )
				using ( GraphicsPath body = Collage.Round( new RectangleF( m, y + h * 0.14f, w, h * 0.86f ), size * 0.06f ) )
				using ( GraphicsPath face = Collage.Round( new RectangleF( m, y + h * 0.3f, w, h * 0.7f ), size * 0.06f ) )
				{
					g.FillPath( back, tab );
					g.FillPath( back, body );
					g.FillPath( front, face );
				}
			}
			return b;
		}

		static Bitmap DriveIcon( int size )
		{
			Bitmap b = new Bitmap( size, size, PixelFormat.Format32bppArgb );
			using ( Graphics g = Graphics.FromImage( b ) )
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				float m = size * 0.08f, h = size * 0.45f;
				using ( Brush body = new SolidBrush( Color.FromArgb( 150, 162, 178 ) ) )
				using ( Brush led = new SolidBrush( Color.FromArgb( 74, 222, 128 ) ) )
				using ( GraphicsPath p = Collage.Round( new RectangleF( m, ( size - h ) / 2, size - 2 * m, h ), size * 0.1f ) )
				{
					g.FillPath( body, p );
					g.FillEllipse( led, size - m - h * 0.55f, size / 2f - h * 0.15f, h * 0.3f, h * 0.3f );
				}
			}
			return b;
		}

		/* thumbnails in the background, in the order of the list; shown a batch at a time */
		void StartThumbs()
		{
			int gen = thumbGen;
			List<string> pics = rows.Where( e => !e.IsDir && IsPic( e.Path ) ).Select( e => e.Path ).Take( MAX_THUMBS ).ToList();
			thumbTotal = pics.Count;
			thumbDone = 0;
			if ( pics.Count == 0 ) return;
			Thread th = new Thread( delegate()
			{
				foreach ( string p in pics )
				{
					if ( gen != thumbGen || IsDisposed ) return;
					Bitmap big, small;
					try
					{
						using ( Bitmap img = WicThumb( p, TH_BIG ) )
						{
							big = Fit( img, TH_BIG, 1f );
							small = Fit( img, TH_SMALL, 1f );
						}
					}
					catch { if ( gen == thumbGen ) Interlocked.Increment( ref thumbDone ); continue; } // counted anyway: the bar reaches the end
					lock ( ready )
					{
						if ( gen != thumbGen ) { big.Dispose(); small.Dispose(); return; } // another folder was opened meanwhile
						ready.Enqueue( new KeyValuePair<string, Bitmap[]>( p, new Bitmap[] { small, big } ) );
						Interlocked.Increment( ref thumbDone );
					}
				}
			} );
			th.IsBackground = true;
			th.Priority = ThreadPriority.BelowNormal;
			th.Start();
		}

		/* the picture already small (longest side = max), read by the Windows image decoder (WIC), not GDI+: GDI+ lets one
		   thread draw at a time, so shrinking big pages with it in the background made every button of the window wait.
		   A JPEG is decoded straight at the small size, which is also much faster; WebP works when Windows has the codec. */
		static Bitmap WicThumb( string path, int max )
		{
			using ( FileStream fs = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ) )
			{
				System.Windows.Media.Imaging.BitmapDecoder dec = System.Windows.Media.Imaging.BitmapDecoder.Create( fs,
					System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile | System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
					System.Windows.Media.Imaging.BitmapCacheOption.None );
				int w0 = dec.Frames[0].PixelWidth, h0 = dec.Frames[0].PixelHeight; // only the header is read for this
				fs.Position = 0;
				System.Windows.Media.Imaging.BitmapImage bi = new System.Windows.Media.Imaging.BitmapImage();
				bi.BeginInit();
				bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
				bi.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
				bi.StreamSource = fs;
				if ( w0 >= h0 ) bi.DecodePixelWidth = Math.Min( max, w0 ); else bi.DecodePixelHeight = Math.Min( max, h0 );
				bi.EndInit();
				bi.Freeze();
				System.Windows.Media.Imaging.FormatConvertedBitmap px = new System.Windows.Media.Imaging.FormatConvertedBitmap( bi, System.Windows.Media.PixelFormats.Pbgra32, null, 0 );
				int w = px.PixelWidth, h = px.PixelHeight;
				Bitmap b = new Bitmap( w, h, PixelFormat.Format32bppPArgb );
				BitmapData data = b.LockBits( new Rectangle( 0, 0, w, h ), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb );
				try { px.CopyPixels( System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * h, data.Stride ); }
				finally { b.UnlockBits( data ); }
				return b;
			}
		}

		void ShowReadyThumbs()
		{
			lock ( ready )
			{
				if ( ready.Count == 0 ) return;
				while ( ready.Count > 0 )
				{
					KeyValuePair<string, Bitmap[]> t = ready.Dequeue();
					thumbSmall[t.Key] = t.Value[0];
					thumbBig[t.Key] = t.Value[1];
				}
			}
			if ( IsRename ) renView.Invalidate();
		}

		/* ---------- copies with the new names, in the background, with the bar under the button ---------- */
		void CopyInBackground( List<Renamer.Step> todo )
		{
			if ( copying ) return;
			copying = true;
			bRename.Enabled = false;
			bOpen3.Visible = false;
			actBar.Reset();
			actBar.Visible = true;
			actBar.Busy = true;
			actHideAt = 0;
			status3.ForeColor = MUTED;
			status3.Text = "Getting ready to copy…";
			string outDir = S.ROut;
			List<KeyValuePair<string, string>> steps = todo.Select( p => new KeyValuePair<string, string>( p.Src, p.Dst ) ).ToList();
			Thread th = new Thread( delegate()
			{
				/* every file to copy (a folder = all its files), and the folders to make */
				List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>();
				List<string> dirs = new List<string>();
				List<string> errors = new List<string>();
				Action<string, string> walk = null;
				walk = delegate( string src, string dst )
				{
					dirs.Add( dst );
					try { foreach ( string f in Directory.GetFiles( src ) ) files.Add( new KeyValuePair<string, string>( f, Path.Combine( dst, Path.GetFileName( f ) ) ) ); } catch ( Exception ex ) { errors.Add( Path.GetFileName( src ) + ": " + ex.Message ); }
					try { foreach ( string d in Directory.GetDirectories( src ) ) walk( d, Path.Combine( dst, Path.GetFileName( d ) ) ); } catch { }
				};
				foreach ( KeyValuePair<string, string> s in steps )
					if ( Directory.Exists( s.Key ) ) walk( s.Key, s.Value );
					else files.Add( s );
				long total = 0, done = 0;
				foreach ( KeyValuePair<string, string> f in files ) try { total += new FileInfo( f.Key ).Length; } catch { }
				int last = 0, n = 0;
				foreach ( string d in dirs ) try { Directory.CreateDirectory( d ); } catch ( Exception ex ) { errors.Add( d + ": " + ex.Message ); }
				foreach ( KeyValuePair<string, string> f in files )
				{
					try { File.Copy( f.Key, f.Value, false ); }
					catch ( Exception ex ) { errors.Add( Path.GetFileName( f.Key ) + ": " + ex.Message ); }
					try { done += new FileInfo( f.Key ).Length; } catch { }
					n++;
					if ( Environment.TickCount - last > 100 || n == files.Count ) // the bar, ten times a second
					{
						last = Environment.TickCount;
						long d = done, t = total;
						int k = n, all = files.Count;
						try
						{
							BeginInvoke( (MethodInvoker) delegate
							{
								actBar.Busy = false;
								actBar.Maximum = 1000;
								actBar.Value = t > 0 ? (int) ( d * 1000 / t ) : k * 1000 / Math.Max( 1, all );
								status3.Text = "Copying   " + k + " / " + all + "   ·   " + Bytes( d ) + " of " + Bytes( t );
							} );
						}
						catch { }
					}
				}
				try
				{
					BeginInvoke( (MethodInvoker) delegate
					{
						copying = false;
						bRename.Enabled = true;
						lastOut = outDir;
						actBar.Busy = false;
						actBar.Maximum = 1;
						actBar.Value = 1;
						actHideAt = Environment.TickCount + 1500;
						status3.ForeColor = errors.Count == 0 ? GREEN : AMBER;
						status3.Text = "✔ " + steps.Count + ( steps.Count == 1 ? " item" : " items" ) + " copied" + ( errors.Count > 0 ? "   ·   " + errors.Count + " failed" : "" );
						bOpen3.Visible = true;
						if ( errors.Count > 0 ) MessageBox.Show( this, string.Join( "\n", errors.Take( 15 ) ), "Some could not be copied", MessageBoxButtons.OK, MessageBoxIcon.Warning );
						if ( string.Equals( Path.GetFullPath( outDir ).TrimEnd( '\\' ), Path.GetFullPath( renFolder ).TrimEnd( '\\' ), StringComparison.OrdinalIgnoreCase ) ) ReloadFolder();
						else UpdateNames();
					} );
				}
				catch { }
			} );
			th.IsBackground = true;
			th.Start();
		}

		/* ---------- your own name: a window with the big picture ---------- */
		void EditName( int index )
		{
			if ( index < 0 || index >= rows.Count ) return;
			Form f = new Form();
			f.Text = "Give it a name";
			f.BackColor = BG;
			f.ForeColor = TEXT;
			f.Font = Font;
			f.ClientSize = new Size( 960, 720 );
			f.MinimumSize = new Size( 640, 480 );
			f.StartPosition = FormStartPosition.CenterParent;
			f.ShowInTaskbar = false;
			f.MinimizeBox = false;
			f.KeyPreview = true;
			f.Icon = Icon;

			PictureBox pic = new PictureBox();
			pic.Dock = DockStyle.Fill;
			pic.SizeMode = PictureBoxSizeMode.Zoom;
			pic.BackColor = BG;
			Panel bottom = new Panel();
			bottom.Dock = DockStyle.Bottom;
			bottom.Height = 158;
			bottom.BackColor = PANEL;
			Label lNow = MakeLabel( bottom, "", 18, 12, MUTED, 9.5f, false );
			Label lPos = MakeLabel( bottom, "", 0, 12, MUTED, 9.5f, true );
			TextBox box = new TextBox();
			box.Location = new Point( 18, 40 );
			box.BackColor = FIELD;
			box.ForeColor = Color.White;
			box.BorderStyle = BorderStyle.FixedSingle;
			box.Font = new Font( "Segoe UI", 15f );
			bottom.Controls.Add( box );
			Label lExt = MakeLabel( bottom, "", 0, 44, MUTED, 14f, false );
			Button bPrev = MakeButton( bottom, "◀  Previous", 18, 94, 130, 44, false );
			Button bPattern = MakeButton( bottom, "Use the pattern", 156, 94, 150, 44, false );
			Button bDone = MakeButton( bottom, "Done", 0, 94, 110, 44, false );
			Button bNext = MakeButton( bottom, "Save  ·  next  ▶", 0, 94, 190, 44, true );
			Label hint = MakeLabel( bottom, "Enter = save and next   ·   Esc = close", 316, 108, MUTED, 8.75f, false );
			bottom.Resize += delegate
			{
				int w = bottom.Width;
				bNext.Left = w - 18 - bNext.Width;
				bDone.Left = bNext.Left - 8 - bDone.Width;
				lPos.Left = w - 18 - lPos.Width;
				lExt.Left = w - 18 - Math.Max( 70, lExt.Width );
				box.Width = lExt.Left - 8 - box.Left;
				hint.Visible = hint.Right < bDone.Left - 8;
			};
			f.Controls.Add( pic );
			f.Controls.Add( bottom );

			int cur = index;
			string proposed = "";
			Action<int> load = delegate( int i )
			{
				cur = i;
				Entry e = rows[i];
				renView.SelectedIndices.Clear();
				renView.SelectedIndices.Add( i );
				renView.EnsureVisible( i );
				Image old = pic.Image;
				pic.Image = null;
				if ( old != null ) old.Dispose();
				string show = e.IsDir ? null : e.Path;
				if ( e.IsDir ) // a folder: its first picture
					try { show = Directory.GetFiles( e.Path ).Where( Marker.IsPicture ).OrderBy( x => x, new NaturalComparer() ).FirstOrDefault(); } catch { }
				if ( show != null && Marker.IsPicture( show ) )
					try { pic.Image = Marker.Thumb( show, 1800 ); } catch { }
				if ( pic.Image == null ) pic.Image = Fit( Pic( e, true ), 256, 1f );

				List<Entry> use = Chosen();
				int at = use.IndexOf( e );
				string ext = e.IsDir ? "" : ( S.RExtLower ? Path.GetExtension( e.Path ).ToLowerInvariant() : Path.GetExtension( e.Path ) );
				string full = Renamer.NewName( e.Path, e.IsDir, Math.Max( 0, at ), Math.Max( 1, use.Count ), S, null );
				proposed = ext != "" && full.EndsWith( ext ) ? full.Substring( 0, full.Length - ext.Length ) : full;
				string mine;
				box.Text = customs.TryGetValue( e.Path, out mine ) ? mine : proposed;
				lNow.Text = "Now:  " + e.Rel + ( customs.ContainsKey( e.Path ) ? "      ✎ your name" : "" );
				lPos.Text = ( i + 1 ) + " / " + rows.Count;
				lExt.Text = ext;
				bPrev.Enabled = i > 0;
				f.Text = "Give it a name  ·  " + e.Rel;
				bottom.PerformLayout();
				lPos.Left = bottom.Width - 18 - lPos.Width;
				box.Width = lExt.Left - 8 - box.Left;
				box.SelectAll();
				box.Focus();
			};
			Action save = delegate
			{
				Entry e = rows[cur];
				string t = box.Text.Trim();
				if ( t == "" || t == proposed ) customs.Remove( e.Path );
				else customs[e.Path] = t;
				if ( t != "" ) e.Checked = true;
				UpdateNames();
			};
			bNext.Click += delegate { save(); if ( cur + 1 < rows.Count ) load( cur + 1 ); else f.Close(); };
			bPrev.Click += delegate { save(); if ( cur > 0 ) load( cur - 1 ); };
			bPattern.Click += delegate { customs.Remove( rows[cur].Path ); UpdateNames(); load( cur ); };
			bDone.Click += delegate { save(); f.Close(); };
			f.KeyDown += delegate( object o, KeyEventArgs e )
			{
				if ( e.KeyCode == Keys.Enter ) { bNext.PerformClick(); e.Handled = e.SuppressKeyPress = true; }
				else if ( e.KeyCode == Keys.Escape ) { f.Close(); e.Handled = true; }
				else if ( e.KeyCode == Keys.PageUp ) { bPrev.PerformClick(); e.Handled = true; }
				else if ( e.KeyCode == Keys.PageDown ) { bNext.PerformClick(); e.Handled = true; }
			};
			f.Shown += delegate { load( cur ); };
			f.ShowDialog( this );
			if ( pic.Image != null ) pic.Image.Dispose();
			f.Dispose();
		}
	}
}
