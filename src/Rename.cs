// ComicsUncut Watermark, Collage & Rename - the RENAME tab (the rest of the window is in Watermark.cs).
//
// - Left: folders (Desktop, Downloads, Pictures, D:\Comics and every drive). Middle: everything in the open folder - its
//   folders, its pictures (with thumbnails) and any other file - as a list or as big thumbnails, a tick box on each.
// - Right, three steps: 1 what (tick all / none / only pictures, show, subfolders), 2 the new names (a pattern with
//   {name} {n} {folder} {date}, numbering, find & replace, letters, spaces), 3 where (same folder = rename, with Undo;
//   another folder = copies with the new names, the originals stay).
// - Any name you want: double-click a file (or F2) - a window with the big picture, type the name, Enter = save and go to
//   the next one. Your own names win over the pattern and are marked "✎ your name".
// - Nothing happens before the list shows it: new names in green, problems (two items with the same name, a file that is
//   already there) in red, and nothing is renamed while there are any.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CuWatermark
{
	/* one thing in the open folder */
	class Entry
	{
		public string Path, Rel;   // full path, the name shown (with the subfolder when "Subfolders too")
		public bool IsDir, Checked;
		public long Size;
		public DateTime Mod;
		/* what the list shows, filled by UpdateNames */
		public string NewName = "", Note = "";
		public int State;          // 0 not ticked, 1 gets a new name, 2 stays the same, 3 has a problem
		public bool Mine;          // a name you typed
	}

	static class Renamer
	{
		public static readonly string[] Cases = { "As written", "lowercase", "UPPERCASE", "Title Case", "web-slug" };
		public static readonly string[] Spaces = { "Keep spaces", "Dash  -", "Underscore  _", "Remove" };
		public static readonly string[] Shows = { "Everything", "Pictures", "Files", "Folders" };
		/* label, pattern, letters ("" = leave as it is) */
		public static readonly string[][] Presets = {
			new string[] { "Ready-made names…", "", "" },
			new string[] { "Folder - 1", "{folder} - {n}", "" },
			new string[] { "Folder 1", "{folder} {n}", "" },
			new string[] { "Only the number: 1, 2, 3…", "{n}", "" },
			new string[] { "Old name (find & replace, letters)", "{name}", "" },
			new string[] { "comicsuncut-com_folder-1  (web)", "comicsuncut.com_{folder}-{n}", "web-slug" },
			new string[] { "Date + old name", "{date} {name}", "" },
		};

		public class Step { public string Src, Dst, Problem; public bool Same, IsDir, Custom; }

		public static bool Exists( string p ) { return File.Exists( p ) || Directory.Exists( p ); }

		static string Date( string p, bool dir )
		{
			try { return ( dir ? Directory.GetLastWriteTime( p ) : File.GetLastWriteTime( p ) ).ToString( "yyyy-MM-dd" ); } catch { return ""; }
		}

		static string Clean( string n )
		{
			char[] bad = Path.GetInvalidFileNameChars();
			return new string( n.Where( c => !bad.Contains( c ) ).ToArray() ).Trim().TrimEnd( '.', ' ' );
		}

		/* the new name (with the extension) of the item at place "index" of "count"; custom = the name typed for it, or null */
		public static string NewName( string path, bool isDir, int index, int count, Settings s, string custom )
		{
			string ext = isDir ? "" : Path.GetExtension( path );
			if ( s.RExtLower ) ext = ext.ToLowerInvariant();
			int last = s.RStart + Math.Max( 1, s.RStep ) * Math.Max( 0, count - 1 );
			string num = ( s.RStart + index * Math.Max( 1, s.RStep ) ).ToString().PadLeft( s.RDigits > 0 ? s.RDigits : last.ToString().Length, '0' );
			string name;
			if ( custom != null )
			{
				name = custom.Trim();
				if ( ext != "" && name.EndsWith( ext, StringComparison.OrdinalIgnoreCase ) ) name = name.Substring( 0, name.Length - ext.Length );
			}
			else
			{
				string pattern = ( s.RPattern ?? "" ).Trim() == "" ? "{name}" : s.RPattern;
				string parent = Path.GetDirectoryName( path );
				name = pattern
					.Replace( "{name}", isDir ? Path.GetFileName( path ) : Path.GetFileNameWithoutExtension( path ) )
					.Replace( "{n}", num )
					.Replace( "{folder}", parent == null ? "" : Path.GetFileName( parent ) )
					.Replace( "{date}", Date( path, isDir ) );
				if ( !string.IsNullOrEmpty( s.RFind ) )
					name = Regex.Replace( name, Regex.Escape( s.RFind ), ( s.RReplace ?? "" ).Replace( "$", "$$" ), RegexOptions.IgnoreCase );
				name = Style( name, s );
			}
			name = Clean( name );
			if ( name == "" ) name = num;
			return name + ext;
		}

		static string Style( string name, Settings s )
		{
			switch ( s.RCase )
			{
				case "lowercase": name = name.ToLowerInvariant(); break;
				case "UPPERCASE": name = name.ToUpperInvariant(); break;
				case "Title Case": name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase( name.ToLowerInvariant() ); break;
				case "web-slug": // "Dashole – Bond 007 Story" -> "dashole-bond-007-story" (no accents, only a-z 0-9 _ -)
					{
						StringBuilder sb = new StringBuilder();
						foreach ( char c in name.Normalize( NormalizationForm.FormD ) )
							if ( CharUnicodeInfo.GetUnicodeCategory( c ) != UnicodeCategory.NonSpacingMark ) sb.Append( c );
						name = Regex.Replace( sb.ToString().ToLowerInvariant(), "[^a-z0-9_]+", "-" );
						return Regex.Replace( name, "-*_-*", "_" ).Trim( '-', '_' );
					}
			}
			switch ( s.RSpaces )
			{
				case "Dash  -": return Regex.Replace( name, @"\s+", "-" );
				case "Underscore  _": return Regex.Replace( name, @"\s+", "_" );
				case "Remove": return Regex.Replace( name, @"\s+", "" );
				default: return Regex.Replace( name, @"\s+", " " );
			}
		}

		/* where every ticked item goes and what is wrong with it */
		public static List<Step> Plan( IList<Entry> items, Settings s, IDictionary<string, string> customs )
		{
			bool outOk = s.ROut != "" && Directory.Exists( s.ROut );
			string outFull = outOk ? Path.GetFullPath( s.ROut ).TrimEnd( '\\' ) + "\\" : "";
			HashSet<string> srcs = new HashSet<string>( items.Select( e => Path.GetFullPath( e.Path ) ), StringComparer.OrdinalIgnoreCase );
			Dictionary<string, int> seen = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
			List<Step> plan = new List<Step>();
			for ( int i = 0; i < items.Count; i++ )
			{
				Entry e = items[i];
				string custom;
				if ( !customs.TryGetValue( e.Path, out custom ) ) custom = null;
				Step p = new Step();
				p.Src = e.Path;
				p.IsDir = e.IsDir;
				p.Custom = custom != null;
				string name = NewName( e.Path, e.IsDir, i, items.Count, s, custom );
				string dir = s.RCopy ? ( outOk ? s.ROut : "" ) : Path.GetDirectoryName( e.Path );
				p.Dst = dir == "" ? name : Path.Combine( dir, name );
				p.Same = !s.RCopy && p.Dst == p.Src;
				int prev;
				if ( seen.TryGetValue( p.Dst, out prev ) ) p.Problem = "same new name as " + Path.GetFileName( items[prev].Path );
				else
				{
					seen[p.Dst] = i;
					if ( dir != "" && !p.Same && Exists( p.Dst ) && ( s.RCopy || !srcs.Contains( Path.GetFullPath( p.Dst ) ) ) )
						p.Problem = "a file with this name is already there";
					else if ( s.RCopy && e.IsDir && outOk && outFull.StartsWith( Path.GetFullPath( e.Path ).TrimEnd( '\\' ) + "\\", StringComparison.OrdinalIgnoreCase ) )
						p.Problem = "a folder cannot be copied into itself";
				}
				plan.Add( p );
			}
			return plan;
		}

		static void MoveAny( string a, string b )
		{
			if ( Directory.Exists( a ) ) Directory.Move( a, b );
			else File.Move( a, b );
		}

		/* renames in two rounds (first to temporary names, then to the new ones), so 1->2 and 2->1 in the same batch work;
		   when anything fails, everything already moved is put back and the error is thrown */
		public static void Move( IList<KeyValuePair<string, string>> pairs )
		{
			List<KeyValuePair<string, string>> done = new List<KeyValuePair<string, string>>();
			try
			{
				List<string> temps = new List<string>();
				foreach ( KeyValuePair<string, string> p in pairs )
				{
					string tmp = Path.Combine( Path.GetDirectoryName( p.Key ), "~ren-" + Guid.NewGuid().ToString( "N" ).Substring( 0, 12 ) + ( Directory.Exists( p.Key ) ? "" : Path.GetExtension( p.Key ) ) );
					MoveAny( p.Key, tmp );
					done.Add( new KeyValuePair<string, string>( p.Key, tmp ) );
					temps.Add( tmp );
				}
				for ( int i = 0; i < pairs.Count; i++ )
				{
					if ( Exists( pairs[i].Value ) ) throw new IOException( Path.GetFileName( pairs[i].Value ) + " is already there" );
					MoveAny( temps[i], pairs[i].Value );
					done.Add( new KeyValuePair<string, string>( temps[i], pairs[i].Value ) );
				}
			}
			catch
			{
				for ( int i = done.Count - 1; i >= 0; i-- )
					try { MoveAny( done[i].Value, done[i].Key ); } catch { }
				throw;
			}
		}

		/* a copy with the new name; a folder is copied with everything in it */
		public static void CopyAny( string src, string dst )
		{
			if ( Directory.Exists( src ) )
			{
				Directory.CreateDirectory( dst );
				foreach ( string f in Directory.GetFiles( src ) ) File.Copy( f, Path.Combine( dst, Path.GetFileName( f ) ), false );
				foreach ( string d in Directory.GetDirectories( src ) ) CopyAny( d, Path.Combine( dst, Path.GetFileName( d ) ) );
			}
			else File.Copy( src, dst, false );
		}
	}

	partial class MainForm
	{
		static readonly Color RED = Color.FromArgb( 248, 113, 113 );
		const int TH_BIG = 128, TH_SMALL = 28, MAX_ITEMS = 20000, MAX_THUMBS = 4000;

		[DllImport( "user32.dll", CharSet = CharSet.Unicode )]
		static extern IntPtr SendMessage( IntPtr h, int msg, IntPtr w, string l );

		[DllImport( "uxtheme.dll", CharSet = CharSet.Unicode )]
		static extern int SetWindowTheme( IntPtr h, string app, string id );

		Panel renRight, renLeft, renWrap, renBar, pWhere;
		TreeView renTree;
		ListView renView;
		ImageList renTreeIcons;
		Label renPath, renParent, lRenCount, lRenOut, status3;
		TextBox tPattern, tFind, tRepl, tSearch;
		NumericUpDown nStart, nStep;
		ComboBox cPreset, cDigits, cCase, cSpaces, cShow;
		CheckBox kExt, kSub;
		Button bViewList, bViewThumbs, bSame, bOther, bRename, bUndo, bOpen3, bUp;
		string renFolder = "";
		readonly List<Entry> entries = new List<Entry>();
		readonly Dictionary<string, string> customs = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase ); // path -> the name you typed
		List<Renamer.Step> renPlan = new List<Renamer.Step>();
		List<KeyValuePair<string, string>> undo; // the last rename: new path -> old path
		int thumbGen;
		int sortCol;
		bool sortDesc, renFilling;

		/* ---------- building ---------- */
		Label StepHead( Control parent, string nr, string title, int y )
		{
			Label n = new Label();
			n.Text = nr;
			n.Size = new Size( 22, 22 );
			n.Location = new Point( 8, y );
			n.TextAlign = ContentAlignment.MiddleCenter;
			n.BackColor = PINK;
			n.ForeColor = Color.White;
			n.Font = new Font( "Segoe UI", 9f, FontStyle.Bold );
			parent.Controls.Add( n );
			return MakeLabel( parent, title, 36, y + 1, TEXT, 10f, true );
		}

		Panel Card( FlowLayoutPanel column, int height )
		{
			Panel p = Section( column, height ); // a rounded card (Section)
			p.Margin = new Padding( 0, 0, 0, 10 );
			return p;
		}

		void SmallButton( Button b )
		{
			b.Font = new Font( "Segoe UI Semibold", 8.75f );
		}

		/* the three steps on the right */
		void BuildRenamePanel()
		{
			rightRen = Column();

			/* 1  what */
			Panel p1 = Card( rightRen, 112 );
			StepHead( p1, "1", "CHOOSE WHAT", 10 );
			lRenCount = MakeLabel( p1, "", 36, 34, MUTED, 8.75f, false );
			Button all = MakeButton( p1, "✔  All", 8, 60, 84, 30, false );
			Button none = MakeButton( p1, "None", 98, 60, 84, 30, false );
			Button pics = MakeButton( p1, "Pictures", 188, 60, 86, 30, false );
			foreach ( Button b in new Button[] { all, none, pics } ) SmallButton( b );
			all.Click += delegate { TickAll( e => true ); };
			none.Click += delegate { TickAll( e => false ); };
			pics.Click += delegate { TickAll( e => !e.IsDir && Marker.IsPicture( e.Path ) ); };
			MakeLabel( p1, "Double-click a folder to open it", 8, 94, MUTED, 8f, false );

			/* 2  names */
			Panel p2 = Card( rightRen, 236 );
			StepHead( p2, "2", "NEW NAMES", 10 );
			tPattern = MakeBox( p2, 8, 40, 266, S.RPattern );
			tPattern.Font = new Font( "Segoe UI", 11.5f );
			int x = 8;
			foreach ( string tok in new string[] { "{name}", "{n}", "{folder}", "{date}" } )
			{
				Button b = MakeButton( p2, tok, x, 76, 62, 26, false );
				SmallButton( b );
				b.Click += delegate
				{
					int at = tPattern.SelectionStart;
					tPattern.Text = tPattern.Text.Insert( at, tok );
					tPattern.SelectionStart = at + tok.Length;
					tPattern.Focus();
				};
				x += 68;
			}
			MakeLabel( p2, "old name   ·   number   ·   folder   ·   date", 12, 104, MUTED, 7.75f, false );
			cPreset = MakeCombo( p2, 8, 126, 266, Renamer.Presets.Select( p => (object) p[0] ).ToArray(), Renamer.Presets[0][0] );
			cPreset.SelectedIndexChanged += delegate
			{
				if ( cPreset.SelectedIndex <= 0 ) return;
				string[] p = Renamer.Presets[cPreset.SelectedIndex];
				tPattern.Text = p[1];
				if ( p[2] != "" ) cCase.SelectedItem = p[2];
				cPreset.SelectedIndex = 0;
			};
			Label own = MakeLabel( p2, "✎  Your own name for one file:\n     double-click it (or F2)", 8, 162, GREEN, 8.75f, false );
			Button forget = MakeButton( p2, "Forget my own names", 8, 200, 266, 28, false );
			SmallButton( forget );
			forget.Click += delegate { customs.Clear(); QueueRedraw(); };

			/* numbering, find & replace, letters */
			Panel p2b = Card( rightRen, 254 );
			MakeLabel( p2b, "NUMBER", 8, 8, MUTED, 8.5f, true );
			MakeLabel( p2b, "START", 8, 30, MUTED, 8f, false );
			MakeLabel( p2b, "STEP", 100, 30, MUTED, 8f, false );
			MakeLabel( p2b, "DIGITS", 192, 30, MUTED, 8f, false );
			nStart = MakeNum( p2b, 8, 48, 84, 0, 999999, S.RStart );
			nStep = MakeNum( p2b, 100, 48, 84, 1, 1000, S.RStep );
			cDigits = MakeCombo( p2b, 192, 47, 82, new object[] { "Auto", "1", "2", "3", "4", "5", "6" }, S.RDigits == 0 ? "Auto" : S.RDigits.ToString() );
			MakeLabel( p2b, "FIND & REPLACE", 8, 84, MUTED, 8.5f, true );
			tFind = MakeBox( p2b, 8, 106, 128, S.RFind );
			MakeLabel( p2b, "→", 140, 108, MUTED, 9f, false );
			tRepl = MakeBox( p2b, 158, 106, 116, S.RReplace );
			Cue( tFind, "find" );
			Cue( tRepl, "replace with" );
			MakeLabel( p2b, "LETTERS", 8, 144, MUTED, 8.5f, true );
			MakeLabel( p2b, "SPACES", 146, 144, MUTED, 8.5f, true );
			cCase = MakeCombo( p2b, 8, 166, 132, Renamer.Cases.Cast<object>().ToArray(), S.RCase );
			cSpaces = MakeCombo( p2b, 146, 166, 128, Renamer.Spaces.Cast<object>().ToArray(), S.RSpaces );
			kExt = MakeCheck( p2b, "Small letters in the extension (.JPG → .jpg)", 8, 202, S.RExtLower );
			kExt.BackColor = CARD;

			/* 3  where */
			pWhere = Card( rightRen, 112 );
			StepHead( pWhere, "3", "WHERE", 10 );
			bSame = MakeButton( pWhere, "Same folder\nrename", 8, 40, 131, 48, false );
			bOther = MakeButton( pWhere, "Other folder\nmake copies", 143, 40, 131, 48, false );
			bSame.Click += delegate { S.RCopy = false; QueueRedraw(); };
			bOther.Click += delegate { S.RCopy = true; if ( S.ROut == "" || !Directory.Exists( S.ROut ) ) ChooseRenOut(); QueueRedraw(); };
			lRenOut = MakeLabel( pWhere, "", 8, 98, TEXT, 8.75f, false );
			lRenOut.AutoSize = false;
			lRenOut.Size = new Size( 266, 36 );
			lRenOut.BackColor = FIELD;
			lRenOut.Padding = new Padding( 6, 3, 6, 3 );
			lRenOut.Cursor = Cursors.Hand;
			lRenOut.Click += delegate { ChooseRenOut(); };
			Button choose = MakeButton( pWhere, "Choose the folder…", 8, 140, 266, 28, false );
			SmallButton( choose );
			choose.Click += delegate { ChooseRenOut(); };
			choose.Tag = "out";

			/* go: fixed under the steps, always in sight */
			rightRen.Dock = DockStyle.Fill;
			renRight = new Panel();
			renRight.Dock = DockStyle.Right;
			renRight.Width = 312;
			renRight.BackColor = PANEL;
			renRight.Visible = false;
			Panel pa = new Panel();
			pa.Dock = DockStyle.Bottom;
			pa.Height = 186;
			pa.BackColor = HEAD;
			pa.Padding = new Padding( 8, 0, 0, 0 );
			pa.Paint += delegate( object o, PaintEventArgs e ) { using ( Pen p = new Pen( Color.FromArgb( 90, 255, 51, 102 ) ) ) e.Graphics.DrawLine( p, 0, 0, pa.Width, 0 ); };
			bRename = MakeButton( pa, "RENAME", 16, 12, 280, 52, true );
			bRename.Click += delegate { DoRename(); };
			actBar = new SlimBar();
			actBar.Location = new Point( 16, 72 );
			actBar.Size = new Size( 280, 6 );
			actBar.Visible = false;
			pa.Controls.Add( actBar );
			status3 = MakeLabel( pa, "", 16, 82, MUTED, 8.75f, false );
			status3.AutoSize = false;
			status3.Size = new Size( 280, 26 );
			bUndo = MakeButton( pa, "↶  Undo last rename", 16, 110, 280, 30, false );
			bUndo.Visible = false;
			bUndo.Click += delegate { DoUndo(); };
			bOpen3 = MakeButton( pa, "Open the folder", 16, 146, 280, 30, false );
			bOpen3.Visible = false;
			bOpen3.Click += delegate { OpenOut(); };

			renRight.Controls.Add( rightRen );
			renRight.Controls.Add( pa );
			Controls.Add( renRight );

			/* folders on the left */
			renLeft = new Panel();
			renLeft.Dock = DockStyle.Left;
			renLeft.Width = SIDE_W;
			renLeft.BackColor = PANEL;
			renLeft.Visible = false;
			MakeLabel( renLeft, "FOLDERS", 14, 14, MUTED, 9f, true );
			renTree = new TreeView();
			renTree.Location = new Point( 10, 40 );
			renTree.BackColor = PANEL;
			renTree.ForeColor = TEXT;
			renTree.BorderStyle = BorderStyle.None;
			renTree.ShowLines = false;
			renTree.FullRowSelect = true;
			renTree.HideSelection = false;
			renTree.ItemHeight = 28;
			renTree.Font = new Font( "Segoe UI", 9.75f );
			renTreeIcons = new ImageList();
			renTreeIcons.ColorDepth = ColorDepth.Depth32Bit;
			renTreeIcons.ImageSize = new Size( 20, 20 );
			renTreeIcons.Images.Add( "folder", FolderIcon( 20 ) );
			renTreeIcons.Images.Add( "drive", DriveIcon( 20 ) );
			renTree.ImageList = renTreeIcons;
			renTree.HandleCreated += delegate { SetWindowTheme( renTree.Handle, "explorer", null ); }; // modern arrows instead of [+]
			renTree.BeforeExpand += delegate( object o, TreeViewCancelEventArgs e ) { FillNode( e.Node ); };
			renTree.NodeMouseClick += delegate( object o, TreeNodeMouseClickEventArgs e ) { if ( e.Node.Tag is string && e.Button == MouseButtons.Left ) OpenFolder( (string) e.Node.Tag ); };
			renTree.AfterSelect += delegate( object o, TreeViewEventArgs e ) { if ( e.Action == TreeViewAction.ByKeyboard && e.Node.Tag is string ) OpenFolder( (string) e.Node.Tag ); };
			renLeft.Controls.Add( renTree );
			renLeft.Resize += delegate { renTree.Size = new Size( renLeft.Width - 14, renLeft.Height - 50 ); };
			string up = Environment.GetEnvironmentVariable( "USERPROFILE" ) ?? "";
			AddRoot( "Desktop", Environment.GetFolderPath( Environment.SpecialFolder.Desktop ) );
			AddRoot( "Downloads", Path.Combine( up, "Downloads" ) );
			AddRoot( "Pictures", Environment.GetFolderPath( Environment.SpecialFolder.MyPictures ) );
			AddRoot( "Comics", @"D:\Comics" );
			foreach ( DriveInfo d in DriveInfo.GetDrives() )
			{
				string label = d.Name.TrimEnd( '\\' );
				try { if ( d.IsReady && d.VolumeLabel != "" ) label += "  " + d.VolumeLabel; } catch { }
				AddRoot( label, d.Name );
			}
			Controls.Add( renLeft );
		}

		/* ---------- folders ---------- */
		void AddRoot( string label, string path )
		{
			if ( string.IsNullOrEmpty( path ) || !Directory.Exists( path ) ) return;
			TreeNode n = new TreeNode( label );
			n.ImageKey = n.SelectedImageKey = path.Length <= 3 ? "drive" : "folder";
			n.Tag = path;
			n.Nodes.Add( new TreeNode( "…" ) ); // filled when opened
			renTree.Nodes.Add( n );
		}

		static bool Hidden( string p )
		{
			try { return ( File.GetAttributes( p ) & ( FileAttributes.Hidden | FileAttributes.System ) ) != 0; } catch { return true; }
		}

		void FillNode( TreeNode n )
		{
			if ( n.Nodes.Count != 1 || n.Nodes[0].Tag != null ) return;
			n.Nodes.Clear();
			try
			{
				foreach ( string d in Directory.GetDirectories( (string) n.Tag ).Where( d => !Hidden( d ) ).OrderBy( d => d, new NaturalComparer() ) )
				{
					TreeNode c = new TreeNode( Path.GetFileName( d ) );
					c.ImageKey = c.SelectedImageKey = "folder";
					c.Tag = d;
					c.Nodes.Add( new TreeNode( "…" ) );
					n.Nodes.Add( c );
				}
			}
			catch { }
		}

		/* toolbar "Open folder…": any folder, also one not in the list on the left */
		void BrowseRenFolder()
		{
			using ( FolderBrowserDialog d = new FolderBrowserDialog() )
			{
				d.Description = "Open a folder to rename what is in it";
				if ( Directory.Exists( renFolder ) ) d.SelectedPath = renFolder;
				if ( d.ShowDialog( this ) == DialogResult.OK ) OpenFolder( d.SelectedPath );
			}
		}

		void ChooseRenOut()
		{
			using ( FolderBrowserDialog d = new FolderBrowserDialog() )
			{
				d.Description = "Where to put the copies with the new names";
				d.ShowNewFolderButton = true;
				if ( Directory.Exists( S.ROut ) ) d.SelectedPath = S.ROut;
				if ( d.ShowDialog( this ) != DialogResult.OK ) return;
				S.ROut = d.SelectedPath;
				S.RCopy = true;
				QueueRedraw();
			}
		}

		/* dropped on the window: a folder opens, files open their folder and only they are ticked */
		void RenameDrop( string[] paths )
		{
			if ( paths.Length == 1 && Directory.Exists( paths[0] ) ) { OpenFolder( paths[0] ); return; }
			string dir = Path.GetDirectoryName( paths[0] );
			if ( dir == null ) return;
			HashSet<string> want = new HashSet<string>( paths, StringComparer.OrdinalIgnoreCase );
			afterRead = e => want.Contains( e.Path ); // the folder is read in the background: tick them when it is in
			OpenFolder( dir );
		}

		void OpenFolder( string path )
		{
			if ( !Directory.Exists( path ) ) return;
			renFolder = path.Length > 3 ? path.TrimEnd( '\\' ) : path;
			S.RFolder = renFolder;
			string name = Path.GetFileName( renFolder ), parent = Path.GetDirectoryName( renFolder );
			renPath.Text = name == "" ? renFolder : name;
			renParent.Text = parent == null ? "" : "in  " + parent;
			bUp.Enabled = Path.GetDirectoryName( renFolder ) != null;
			renFilling = true;
			tSearch.Text = "";
			renFilling = false;
			ReloadFolder();
		}

		static void Walk( string dir, List<string> found, int depth )
		{
			if ( depth > 12 || found.Count > MAX_ITEMS ) return;
			try { found.AddRange( Directory.GetFiles( dir ) ); } catch { }
			try { foreach ( string d in Directory.GetDirectories( dir ) ) if ( !Hidden( d ) ) Walk( d, found, depth + 1 ); } catch { }
		}

		/* ---------- do it ---------- */
		void DoRename()
		{
			ReadSettings();
			UpdateNames();
			if ( renPlan.Count == 0 ) { MessageBox.Show( this, "Tick what you want to rename first (the boxes in the list).", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Information ); return; }
			if ( S.RCopy && ( S.ROut == "" || !Directory.Exists( S.ROut ) ) )
			{
				ChooseRenOut();
				if ( S.ROut == "" || !Directory.Exists( S.ROut ) ) return;
				UpdateNames();
			}
			int bad = renPlan.Count( p => p.Problem != null );
			if ( bad > 0 ) { MessageBox.Show( this, bad + ( bad == 1 ? " new name has" : " new names have" ) + " a problem (in red in the list).\nChange the name (add {n} for a number), give them your own names, or untick them.", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Warning ); return; }
			List<Renamer.Step> todo = renPlan.Where( p => !p.Same ).ToList();
			if ( todo.Count == 0 ) { MessageBox.Show( this, "Nothing to change: the new names are the same as the old ones.", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Information ); return; }
			string what = todo.Count + ( todo.Count == 1 ? " item" : " items" );
			string q = S.RCopy
				? "Copy " + what + " with the new names to\n" + S.ROut + " ?\n\nThe originals stay as they are."
				: "Rename " + what + " in " + renFolder + " ?\n\nYou can undo it with “Undo last rename”.";
			if ( MessageBox.Show( this, q, S.RCopy ? "Copy" : "Rename", MessageBoxButtons.YesNo, MessageBoxIcon.Question ) != DialogResult.Yes ) return;
			SaveSettings();

			Cursor = Cursors.WaitCursor;
			try
			{
				if ( S.RCopy ) { CopyInBackground( todo ); return; }
				List<KeyValuePair<string, string>> pairs = todo.Select( p => new KeyValuePair<string, string>( p.Src, p.Dst ) ).ToList();
				Renamer.Move( pairs );
				undo = pairs.Select( p => new KeyValuePair<string, string>( p.Value, p.Key ) ).ToList();
				foreach ( Renamer.Step p in todo ) customs.Remove( p.Src );
				lastOut = renFolder;
				status3.ForeColor = GREEN;
				status3.Text = "✔ " + what + " renamed";
				ActDone();
				bUndo.Visible = true;
				bOpen3.Visible = true;
				AfterMove( todo.Any( p => p.IsDir ) );
			}
			catch ( Exception ex )
			{
				status3.ForeColor = AMBER;
				status3.Text = "Nothing renamed: " + ex.Message;
			}
			finally { Cursor = Cursors.Default; }
		}

		void DoUndo()
		{
			if ( undo == null ) return;
			Cursor = Cursors.WaitCursor;
			try
			{
				Renamer.Move( undo );
				status3.ForeColor = GREEN;
				status3.Text = "↶ " + undo.Count + ( undo.Count == 1 ? " item" : " items" ) + " back to the old names";
				bool dirs = undo.Any( p => Directory.Exists( p.Value ) );
				undo = null;
				bUndo.Visible = false;
				AfterMove( dirs );
			}
			catch ( Exception ex )
			{
				status3.ForeColor = AMBER;
				status3.Text = "Could not undo: " + ex.Message;
			}
			finally { Cursor = Cursors.Default; }
		}

		/* the list (and the folder tree, when folders were renamed) show the new names */
		void AfterMove( bool dirs )
		{
			if ( dirs )
				foreach ( TreeNode n in AllNodes( renTree.Nodes ) )
					if ( n.Tag is string && string.Equals( ( (string) n.Tag ).TrimEnd( '\\' ), renFolder.TrimEnd( '\\' ), StringComparison.OrdinalIgnoreCase ) )
					{
						n.Collapse();
						n.Nodes.Clear();
						n.Nodes.Add( new TreeNode( "…" ) );
					}
			ReloadFolder();
		}

		static IEnumerable<TreeNode> AllNodes( TreeNodeCollection nodes )
		{
			foreach ( TreeNode n in nodes )
			{
				yield return n;
				foreach ( TreeNode c in AllNodes( n.Nodes ) ) yield return c;
			}
		}
	}
}
