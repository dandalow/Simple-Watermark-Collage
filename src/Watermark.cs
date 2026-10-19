// ComicsUncut Watermark - watermarks and collages.
// Windows Forms, .NET Framework 4.x, C# 5 (built by build.bat with the csc.exe that ships with Windows).
//
// WATERMARK tab
// - Image mark (a picture you choose, a transparent PNG works best) or text mark (any installed font, bold/italic, colour,
//   outline, shadow). Position, size, opacity, margin, angle and JPG quality. Live preview.
// COLLAGE tab
// - Up to 10 pictures (the selected ones, or the first 10 in the list) in one of 10 templates; spacing between the
//   pictures, outer border, rounded corners, background colour, format and width; optional watermark on the collage.
// RENAME tab (Rename.cs)
// - A small file browser: folders on the left, every folder / picture / file of the open folder in the middle (list or
//   big thumbnails), tick what to rename. Names from a pattern or typed one by one; same folder = rename (with Undo),
//   another folder = copies with the new names.
// All
// - Add pictures with the buttons or drag & drop them (or whole folders) onto the window or onto Watermark.exe.
// - The originals are never changed: everything is saved in a folder you choose (never the pictures' own folder for
//   watermarks); a file that is already there is never replaced, the new one gets " (2)", " (3)"… instead.
// - Settings are remembered in settings.ini next to the .exe.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CuWatermark
{
	static class Program
	{
		[STAThread]
		static int Main( string[] args )
		{
			/* Watermark.exe --apply <pictures or folders>: watermarks without the window, with the saved settings
			   Watermark.exe --collage <pictures>: one collage without the window (testing) */
			if ( args.Length > 0 && args[0] == "--apply" ) return Batch( args.Skip( 1 ) );
			if ( args.Length > 0 && args[0] == "--collage" ) return BatchCollage( args.Skip( 1 ).ToList() );
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault( false );
			Application.Run( new MainForm( args ) );
			return 0;
		}

		static int Batch( IEnumerable<string> paths )
		{
			Settings s = Settings.Load();
			Bitmap mark = null;
			if ( s.Mode != "text" )
			{
				if ( s.MarkPath() == "" ) return 2;
				mark = Marker.Load( s.MarkPath() );
			}
			int done = 0, failed = 0;
			foreach ( string p in paths )
			{
				IEnumerable<string> list = Directory.Exists( p ) ? Directory.GetFiles( p ).Where( Marker.IsPicture ) : new string[] { p };
				foreach ( string f in list )
				{
					try
					{
						if ( s.OutFolder == "" || !Directory.Exists( s.OutFolder ) ) throw new Exception( "no output folder chosen" );
						if ( string.Equals( Path.GetFullPath( s.OutFolder ).TrimEnd( '\\' ), Path.GetFullPath( Path.GetDirectoryName( f ) ).TrimEnd( '\\' ), StringComparison.OrdinalIgnoreCase ) )
							throw new Exception( "the output folder is the picture's own folder" );
						string dst = Marker.FreeName( s.OutFolder, Path.GetFileName( f ) );
						using ( Bitmap b = Marker.Load( f ) )
						{
							Marker.Draw( b, mark, s );
							Marker.Save( b, dst, s.Quality );
						}
						done++;
					}
					catch { failed++; }
				}
			}
			if ( mark != null ) mark.Dispose();
			return failed > 0 ? 1 : ( done > 0 ? 0 : 2 );
		}

		static int BatchCollage( List<string> paths )
		{
			Settings s = Settings.Load();
			if ( s.OutFolder == "" || !Directory.Exists( s.OutFolder ) || paths.Count == 0 ) return 2;
			Bitmap mark = s.Mode != "text" && s.MarkPath() != "" ? Marker.Load( s.MarkPath() ) : null;
			List<Bitmap> imgs = paths.Take( Collage.Max ).Select( Marker.Load ).ToList();
			try
			{
				using ( Bitmap c = Collage.Render( imgs, s, s.CWidth, mark ) )
					Marker.Save( c, Marker.FreeName( s.OutFolder, "Collage" + ( s.CType == "PNG" ? ".png" : ".jpg" ) ), s.Quality );
				return 0;
			}
			catch { return 1; }
			finally
			{
				foreach ( Bitmap b in imgs ) b.Dispose();
				if ( mark != null ) mark.Dispose();
			}
		}
	}

	/* ---------- settings.ini ---------- */
	class Settings
	{
		public string AppMode = "watermark"; // last tab: "watermark" | "collage" | "rename"
		public string Mode = "image";       // watermark kind: "image" | "text"
		public string Mark = "";            // the picture used as image mark (chosen with Choose picture…)
		public string Text = "comicsuncut.com";
		public string FontName = "Arial Black";
		public bool Bold = false;
		public bool Italic = false;
		public string TextColor = "FFFFFF";
		public bool Outline = true;
		public string OutlineColor = "000000";
		public int OutlineWidth = 6;        // % of the text height
		public bool Shadow = true;
		public string Position = "Bottom right";
		public int Size = 18;               // % of the picture width
		public int Opacity = 90;            // %
		public int Margin = 5;              // half percents of the shorter side (5 = 2.5%)
		public int Angle = 0;               // degrees
		public int Quality = 92;            // JPG
		public string OutFolder = "";
		/* collage */
		public int CTemplate = 0;
		public string CFormat = "Portrait 4:5";
		public int CWidth = 2048;
		public int CSpacing = 12;           // thousandths of the collage width
		public int COuter = 14;
		public int CRadius = 0;
		public string CBg = "0B0D10";
		public bool CFit = false;           // whole pictures (letterboxed) instead of filling the cells
		public bool CMark = true;           // watermark on the collage
		public string CType = "JPG";
		/* rename */
		public string RPattern = "{folder} - {n}";
		public int RStart = 1;
		public int RStep = 1;
		public int RDigits = 0;             // 0 = as many as the biggest number needs
		public string RFind = "";
		public string RReplace = "";
		public string RCase = "As written";
		public string RSpaces = "Keep spaces";
		public bool RExtLower = true;
		public bool RCopy = false;          // copy to ROut with the new names instead of renaming in place
		public string RFolder = "";         // the folder open in the rename tab
		public string ROut = "";            // where the copies go
		public string RView = "list";       // "list" | "thumbs"
		public string RShow = "Everything"; // Everything | Pictures | Files | Folders
		public bool RSub = false;           // files of the subfolders too
		/* the window, as it was left */
		public int WinX, WinY, WinW, WinH;
		public bool WinMax;

		static string IniPath { get { return Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "settings.ini" ); } }

		public string MarkPath()
		{
			return Mark != "" && File.Exists( Mark ) ? Mark : "";
		}

		public Settings Clone()
		{
			return (Settings) MemberwiseClone();
		}

		public static Settings Load()
		{
			Settings s = new Settings();
			try
			{
				if ( !File.Exists( IniPath ) ) return s;
				foreach ( string line in File.ReadAllLines( IniPath ) )
				{
					int eq = line.IndexOf( '=' );
					if ( eq < 1 ) continue;
					string k = line.Substring( 0, eq ).Trim(), v = line.Substring( eq + 1 );
					string t = v.Trim();
					int n;
					switch ( k )
					{
						case "app_mode": s.AppMode = t == "collage" || t == "rename" ? t : "watermark"; break;
						case "mode": s.Mode = t == "text" ? "text" : "image"; break;
						case "mark": s.Mark = t; break;
						case "text": s.Text = v; break;
						case "font": if ( t != "" ) s.FontName = t; break;
						case "bold": s.Bold = t == "1"; break;
						case "italic": s.Italic = t == "1"; break;
						case "color": s.TextColor = t; break;
						case "outline": s.Outline = t == "1"; break;
						case "outline_color": s.OutlineColor = t; break;
						case "outline_width": if ( int.TryParse( t, out n ) ) s.OutlineWidth = n; break;
						case "shadow": s.Shadow = t == "1"; break;
						case "position": s.Position = t; break;
						case "size": if ( int.TryParse( t, out n ) ) s.Size = n; break;
						case "opacity": if ( int.TryParse( t, out n ) ) s.Opacity = n; break;
						case "margin": if ( int.TryParse( t, out n ) ) s.Margin = n; break;
						case "angle": if ( int.TryParse( t, out n ) ) s.Angle = n; break;
						case "quality": if ( int.TryParse( t, out n ) ) s.Quality = n; break;
						case "out_folder": s.OutFolder = t; break;
						case "c_template": if ( int.TryParse( t, out n ) ) s.CTemplate = Math.Max( 0, Math.Min( Collage.Names.Length - 1, n ) ); break;
						case "c_format": if ( Collage.Formats.Contains( t ) ) s.CFormat = t; break;
						case "c_width": if ( int.TryParse( t, out n ) ) s.CWidth = n; break;
						case "c_spacing": if ( int.TryParse( t, out n ) ) s.CSpacing = n; break;
						case "c_outer": if ( int.TryParse( t, out n ) ) s.COuter = n; break;
						case "c_radius": if ( int.TryParse( t, out n ) ) s.CRadius = n; break;
						case "c_bg": s.CBg = t; break;
						case "c_fit": s.CFit = t == "1"; break;
						case "c_mark": s.CMark = t == "1"; break;
						case "c_type": s.CType = t == "PNG" ? "PNG" : "JPG"; break;
						case "r_pattern": s.RPattern = v; break;
						case "r_start": if ( int.TryParse( t, out n ) ) s.RStart = Math.Max( 0, n ); break;
						case "r_step": if ( int.TryParse( t, out n ) ) s.RStep = Math.Max( 1, n ); break;
						case "r_digits": if ( int.TryParse( t, out n ) ) s.RDigits = Math.Max( 0, Math.Min( 6, n ) ); break;
						case "r_find": s.RFind = v; break;
						case "r_replace": s.RReplace = v; break;
						case "r_case": if ( Renamer.Cases.Contains( t ) ) s.RCase = t; break;
						case "r_spaces": if ( Renamer.Spaces.Contains( t ) ) s.RSpaces = t; break;
						case "r_ext_lower": s.RExtLower = t == "1"; break;
						case "r_folder": s.RFolder = t; break;
						case "r_out": s.ROut = t; break;
						case "r_view": s.RView = t == "thumbs" ? "thumbs" : "list"; break;
						case "r_show": if ( Renamer.Shows.Contains( t ) ) s.RShow = t; break;
						case "r_sub": s.RSub = t == "1"; break;
						case "win":
							{
								string[] p = t.Split( ',' );
								int x, y, w, h;
								if ( p.Length == 4 && int.TryParse( p[0], out x ) && int.TryParse( p[1], out y ) && int.TryParse( p[2], out w ) && int.TryParse( p[3], out h ) ) { s.WinX = x; s.WinY = y; s.WinW = w; s.WinH = h; }
								break;
							}
						case "win_max": s.WinMax = t == "1"; break;
						case "r_copy": s.RCopy = t == "1"; break;
					}
				}
			}
			catch { }
			return s;
		}

		public void Save()
		{
			try
			{
				File.WriteAllLines( IniPath, new string[] {
					"app_mode=" + AppMode, "mode=" + Mode, "mark=" + Mark, "text=" + Text.Replace( "\r", " " ).Replace( "\n", " " ), "font=" + FontName,
					"bold=" + ( Bold ? "1" : "0" ), "italic=" + ( Italic ? "1" : "0" ), "color=" + TextColor,
					"outline=" + ( Outline ? "1" : "0" ), "outline_color=" + OutlineColor, "outline_width=" + OutlineWidth,
					"shadow=" + ( Shadow ? "1" : "0" ), "position=" + Position, "size=" + Size, "opacity=" + Opacity,
					"margin=" + Margin, "angle=" + Angle, "quality=" + Quality, "out_folder=" + OutFolder,
					"c_template=" + CTemplate, "c_format=" + CFormat, "c_width=" + CWidth, "c_spacing=" + CSpacing, "c_outer=" + COuter,
					"c_radius=" + CRadius, "c_bg=" + CBg, "c_fit=" + ( CFit ? "1" : "0" ), "c_mark=" + ( CMark ? "1" : "0" ), "c_type=" + CType,
					"r_pattern=" + RPattern, "r_start=" + RStart, "r_step=" + RStep, "r_digits=" + RDigits, "r_find=" + RFind, "r_replace=" + RReplace,
					"r_case=" + RCase, "r_spaces=" + RSpaces, "r_ext_lower=" + ( RExtLower ? "1" : "0" ),
					"r_copy=" + ( RCopy ? "1" : "0" ), "r_folder=" + RFolder, "r_out=" + ROut, "r_view=" + RView, "r_show=" + RShow, "r_sub=" + ( RSub ? "1" : "0" ),
					"win=" + WinX + "," + WinY + "," + WinW + "," + WinH, "win_max=" + ( WinMax ? "1" : "0" ) } );
			}
			catch { }
		}
	}

	/* ---------- watermark work ---------- */
	static class Marker
	{
		public static readonly string[] Ext = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

		public static bool IsPicture( string path )
		{
			return Ext.Contains( Path.GetExtension( path ).ToLowerInvariant() );
		}

		public static Color Hex( string hex, Color fallback )
		{
			try { return ColorTranslator.FromHtml( "#" + hex.TrimStart( '#' ) ); } catch { return fallback; }
		}

		public static string ToHex( Color c )
		{
			return c.R.ToString( "X2" ) + c.G.ToString( "X2" ) + c.B.ToString( "X2" );
		}

		/* loads without keeping the file locked */
		public static Bitmap Load( string path )
		{
			byte[] bytes = File.ReadAllBytes( path );
			using ( MemoryStream ms = new MemoryStream( bytes ) )
			using ( Image img = Image.FromStream( ms ) )
			{
				Bitmap bmp = new Bitmap( img.Width, img.Height, PixelFormat.Format32bppArgb );
				bmp.SetResolution( img.HorizontalResolution, img.VerticalResolution );
				using ( Graphics g = Graphics.FromImage( bmp ) )
				{
					g.DrawImage( img, 0, 0, img.Width, img.Height );
				}
				return bmp;
			}
		}

		/* a smaller copy (longest side = max) for previews */
		public static Bitmap Thumb( string path, int max )
		{
			using ( Bitmap full = Load( path ) )
			{
				double k = Math.Min( 1.0, max / (double) Math.Max( full.Width, full.Height ) );
				int w = Math.Max( 1, (int) ( full.Width * k ) ), h = Math.Max( 1, (int) ( full.Height * k ) );
				Bitmap t = new Bitmap( w, h, PixelFormat.Format32bppArgb );
				using ( Graphics g = Graphics.FromImage( t ) )
				{
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.DrawImage( full, 0, 0, w, h );
				}
				return t;
			}
		}

		/* where the mark goes on a W x H picture (mark = its own width and height, only the proportion matters) */
		public static Rectangle Place( int W, int H, SizeF mark, Settings s )
		{
			int mw = Math.Max( Math.Min( 40, W / 2 ), (int) Math.Round( W * s.Size / 100.0 ) );
			int mh = Math.Max( 1, (int) Math.Round( mark.Height * (double) mw / Math.Max( 1f, mark.Width ) ) );
			int gap = (int) Math.Round( Math.Min( W, H ) * s.Margin / 200.0 );
			int x, y;
			switch ( s.Position )
			{
				case "Bottom left": x = gap; y = H - mh - gap; break;
				case "Top right": x = W - mw - gap; y = gap; break;
				case "Top left": x = gap; y = gap; break;
				case "Bottom center": x = ( W - mw ) / 2; y = H - mh - gap; break;
				case "Top center": x = ( W - mw ) / 2; y = gap; break;
				case "Center": x = ( W - mw ) / 2; y = ( H - mh ) / 2; break;
				default: x = W - mw - gap; y = H - mh - gap; break; // Bottom right
			}
			return new Rectangle( x, y, mw, mh );
		}

		public static void Quality( Graphics g )
		{
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;
			g.CompositingQuality = CompositingQuality.HighQuality;
			g.SmoothingMode = SmoothingMode.AntiAlias;
		}

		public static void Draw( Bitmap target, Bitmap mark, Settings s )
		{
			if ( s.Mode == "text" ) DrawText( target, s );
			else if ( mark != null ) DrawImage( target, mark, s );
		}

		static void DrawImage( Bitmap target, Bitmap mark, Settings s )
		{
			Rectangle r = Place( target.Width, target.Height, mark.Size, s );
			using ( Graphics g = Graphics.FromImage( target ) )
			using ( ImageAttributes ia = new ImageAttributes() )
			{
				Quality( g );
				ColorMatrix cm = new ColorMatrix();
				cm.Matrix33 = s.Opacity / 100f;
				ia.SetColorMatrix( cm );
				ia.SetWrapMode( WrapMode.TileFlipXY ); // no faint border around the scaled mark
				g.TranslateTransform( r.X + r.Width / 2f, r.Y + r.Height / 2f );
				g.RotateTransform( s.Angle );
				g.DrawImage( mark, new Rectangle( -r.Width / 2, -r.Height / 2, r.Width, r.Height ), 0, 0, mark.Width, mark.Height, GraphicsUnit.Pixel, ia );
			}
		}

		static FontFamily Family( string name )
		{
			try { return new FontFamily( name ); } catch { return new FontFamily( GenericFontFamilies.SansSerif ); }
		}

		/* the text as a shape: same look at any size, crisp outline and shadow */
		static void DrawText( Bitmap target, Settings s )
		{
			string text = ( s.Text ?? "" ).Trim();
			if ( text == "" ) return;
			using ( FontFamily ff = Family( s.FontName ) )
			using ( GraphicsPath path = new GraphicsPath() )
			{
				FontStyle st = ( s.Bold ? FontStyle.Bold : FontStyle.Regular ) | ( s.Italic ? FontStyle.Italic : FontStyle.Regular );
				if ( !ff.IsStyleAvailable( st ) ) st = ff.IsStyleAvailable( FontStyle.Regular ) ? FontStyle.Regular : ( ff.IsStyleAvailable( FontStyle.Bold ) ? FontStyle.Bold : FontStyle.Italic );
				path.AddString( text, ff, (int) st, 200f, PointF.Empty, StringFormat.GenericTypographic );
				RectangleF b = path.GetBounds();
				if ( b.Width <= 0 || b.Height <= 0 ) return;

				Rectangle r = Place( target.Width, target.Height, b.Size, s );
				float k = r.Width / b.Width;
				using ( Matrix m = new Matrix() ) // move to the spot, turn, scale (applied in reverse order)
				{
					m.Translate( r.X + r.Width / 2f, r.Y + r.Height / 2f );
					m.Rotate( s.Angle );
					m.Scale( k, k );
					m.Translate( -( b.X + b.Width / 2f ), -( b.Y + b.Height / 2f ) );
					path.Transform( m );
				}
				int a = (int) Math.Round( 255 * Math.Max( 0, Math.Min( 100, s.Opacity ) ) / 100.0 );
				using ( Graphics g = Graphics.FromImage( target ) )
				{
					Quality( g );
					if ( s.Shadow )
					{
						using ( GraphicsPath sp = (GraphicsPath) path.Clone() )
						using ( Matrix mm = new Matrix() )
						using ( Brush sb = new SolidBrush( Color.FromArgb( (int) ( a * 0.55 ), 0, 0, 0 ) ) )
						{
							float off = Math.Max( 1f, r.Height * 0.07f );
							mm.Translate( off, off );
							sp.Transform( mm );
							g.FillPath( sb, sp );
						}
					}
					if ( s.Outline && s.OutlineWidth > 0 )
					{
						using ( Pen p = new Pen( Color.FromArgb( a, Hex( s.OutlineColor, Color.Black ) ), Math.Max( 1f, r.Height * s.OutlineWidth / 100f ) ) )
						{
							p.LineJoin = LineJoin.Round;
							g.DrawPath( p, path );
						}
					}
					using ( Brush fb = new SolidBrush( Color.FromArgb( a, Hex( s.TextColor, Color.White ) ) ) )
						g.FillPath( fb, path );
				}
			}
		}

		/* "page.jpg", or "page (2).jpg", "page (3).jpg"… when that name is already taken in the folder */
		public static string FreeName( string dir, string name )
		{
			string path = Path.Combine( dir, name );
			string stem = Path.GetFileNameWithoutExtension( name ), ext = Path.GetExtension( name );
			for ( int n = 2; File.Exists( path ); n++ )
				path = Path.Combine( dir, stem + " (" + n + ")" + ext );
			return path;
		}

		public static void Save( Bitmap bmp, string path, int quality )
		{
			string ext = Path.GetExtension( path ).ToLowerInvariant();
			if ( ext == ".jpg" || ext == ".jpeg" )
			{
				ImageCodecInfo enc = ImageCodecInfo.GetImageEncoders().First( e => e.FormatID == ImageFormat.Jpeg.Guid );
				using ( EncoderParameters ep = new EncoderParameters( 1 ) )
				using ( Bitmap rgb = new Bitmap( bmp.Width, bmp.Height, PixelFormat.Format24bppRgb ) )
				{
					ep.Param[0] = new EncoderParameter( System.Drawing.Imaging.Encoder.Quality, (long) quality );
					rgb.SetResolution( bmp.HorizontalResolution, bmp.VerticalResolution );
					using ( Graphics g = Graphics.FromImage( rgb ) )
					{
						g.Clear( Color.White );
						g.DrawImage( bmp, 0, 0, bmp.Width, bmp.Height );
					}
					rgb.Save( path, enc, ep );
				}
			}
			else if ( ext == ".png" ) bmp.Save( path, ImageFormat.Png );
			else if ( ext == ".bmp" ) bmp.Save( path, ImageFormat.Bmp );
			else if ( ext == ".gif" ) bmp.Save( path, ImageFormat.Gif );
			else bmp.Save( path, ImageFormat.Tiff );
		}
	}

	/* ---------- collages ---------- */
	static class Collage
	{
		public const int Max = 10;
		public static readonly string[] Names = { "Grid", "Rows", "Columns", "Big top", "Big left", "Spotlight", "Two + row", "Bricks", "Spiral", "Magazine" };
		public static readonly string[] Formats = { "Square 1:1", "Portrait 4:5", "Story 9:16", "Landscape 3:2", "Wide 16:9" };
		public static readonly int[] Widths = { 1080, 1600, 2048, 3000 };

		public static SizeF Ratio( string f )
		{
			switch ( f )
			{
				case "Square 1:1": return new SizeF( 1, 1 );
				case "Story 9:16": return new SizeF( 9, 16 );
				case "Landscape 3:2": return new SizeF( 3, 2 );
				case "Wide 16:9": return new SizeF( 16, 9 );
				default: return new SizeF( 4, 5 );
			}
		}

		public static Size Canvas( Settings s, int width )
		{
			SizeF r = Ratio( s.CFormat );
			return new Size( width, Math.Max( 1, (int) Math.Round( width * r.Height / r.Width ) ) );
		}

		/* n split into parts as evenly as possible (10 in 3 -> 4,3,3) */
		static int[] Balanced( int n, int parts )
		{
			parts = Math.Max( 1, Math.Min( parts, n ) );
			int[] c = new int[parts];
			for ( int i = 0; i < parts; i++ ) c[i] = n / parts + ( i < n % parts ? 1 : 0 );
			return c;
		}

		static void Row( List<RectangleF> L, RectangleF r, int k )
		{
			for ( int i = 0; i < k; i++ ) L.Add( new RectangleF( r.X + r.Width * i / k, r.Y, r.Width / k, r.Height ) );
		}

		static void Col( List<RectangleF> L, RectangleF r, int k )
		{
			for ( int i = 0; i < k; i++ ) L.Add( new RectangleF( r.X, r.Y + r.Height * i / k, r.Width, r.Height / k ) );
		}

		static void Rows( List<RectangleF> L, RectangleF r, int[] counts )
		{
			for ( int i = 0; i < counts.Length; i++ )
				Row( L, new RectangleF( r.X, r.Y + r.Height * i / counts.Length, r.Width, r.Height / counts.Length ), counts[i] );
		}

		/* near-square grid; a shorter last row is stretched to the full width */
		static void Grid( List<RectangleF> L, RectangleF r, int m )
		{
			if ( m <= 0 ) return;
			int cols = (int) Math.Ceiling( Math.Sqrt( m ) );
			int rows = (int) Math.Ceiling( m / (double) cols );
			int[] counts = new int[rows];
			int left = m;
			for ( int i = 0; i < rows; i++ ) { counts[i] = Math.Min( cols, left ); left -= counts[i]; }
			Rows( L, r, counts );
		}

		/* cells of a template for n pictures, in a 0..1 square (the first picture gets the "big" cell) */
		public static List<RectangleF> Layout( int t, int n )
		{
			List<RectangleF> L = new List<RectangleF>();
			RectangleF full = new RectangleF( 0, 0, 1, 1 );
			if ( n <= 1 ) { L.Add( full ); return L; }
			int lines = n <= 3 ? 1 : ( n <= 6 ? 2 : 3 );
			switch ( t )
			{
				case 1: // Rows
					Rows( L, full, Balanced( n, lines ) );
					break;
				case 2: // Columns
					{
						int[] c = Balanced( n, lines );
						for ( int i = 0; i < c.Length; i++ ) Col( L, new RectangleF( (float) i / c.Length, 0, 1f / c.Length, 1 ), c[i] );
						break;
					}
				case 3: // Big top
					{
						L.Add( new RectangleF( 0, 0, 1, 0.62f ) );
						int m = n - 1;
						RectangleF b = new RectangleF( 0, 0.62f, 1, 0.38f );
						if ( m <= 4 ) Row( L, b, m ); else Rows( L, b, Balanced( m, 2 ) );
						break;
					}
				case 4: // Big left
					{
						L.Add( new RectangleF( 0, 0, 0.6f, 1 ) );
						int m = n - 1;
						RectangleF b = new RectangleF( 0.6f, 0, 0.4f, 1 );
						if ( m <= 3 ) Col( L, b, m ); else Rows( L, b, Balanced( m, (int) Math.Ceiling( m / 2.0 ) ) ); // two per row
						break;
					}
				case 5: // Spotlight: big in the middle, the rest above and below
					{
						if ( n == 2 ) { Row( L, full, 2 ); break; }
						int m = n - 1, top = ( m + 1 ) / 2, bot = m - top;
						float ht = 0.22f, hb = bot > 0 ? 0.22f : 0f;
						L.Add( new RectangleF( 0, ht, 1, 1 - ht - hb ) );
						Row( L, new RectangleF( 0, 0, 1, ht ), top );
						if ( bot > 0 ) Row( L, new RectangleF( 0, 1 - hb, 1, hb ), bot );
						break;
					}
				case 6: // Two big + row
					{
						if ( n <= 2 ) { Row( L, full, n ); break; }
						Row( L, new RectangleF( 0, 0, 1, 0.6f ), 2 );
						int m = n - 2;
						RectangleF b = new RectangleF( 0, 0.6f, 1, 0.4f );
						if ( m <= 4 ) Row( L, b, m ); else Rows( L, b, Balanced( m, 2 ) );
						break;
					}
				case 7: // Bricks: rows of 2 and 3 in turn
					{
						List<int> c = new List<int>();
						int left = n;
						bool two = true;
						while ( left > 0 ) { int k = Math.Min( two ? 2 : 3, left ); c.Add( k ); left -= k; two = !two; }
						Rows( L, full, c.ToArray() );
						break;
					}
				case 8: // Spiral: three cuts turning around, the rest in a small grid
					{
						RectangleF r = full;
						int steps = Math.Min( n - 1, 3 );
						for ( int i = 0; i < steps; i++ )
						{
							const float f = 0.55f;
							switch ( i )
							{
								case 0: L.Add( new RectangleF( r.X, r.Y, r.Width * f, r.Height ) ); r = new RectangleF( r.X + r.Width * f, r.Y, r.Width * ( 1 - f ), r.Height ); break;
								case 1: L.Add( new RectangleF( r.X, r.Y, r.Width, r.Height * f ) ); r = new RectangleF( r.X, r.Y + r.Height * f, r.Width, r.Height * ( 1 - f ) ); break;
								default: L.Add( new RectangleF( r.X + r.Width * ( 1 - f ), r.Y, r.Width * f, r.Height ) ); r = new RectangleF( r.X, r.Y, r.Width * ( 1 - f ), r.Height ); break;
							}
						}
						Grid( L, r, n - steps );
						break;
					}
				case 9: // Magazine: big left, wide top right, grid below it
					{
						L.Add( new RectangleF( 0, 0, 0.5f, 1 ) );
						int m = n - 1;
						if ( m == 1 ) { L.Add( new RectangleF( 0.5f, 0, 0.5f, 1 ) ); break; }
						L.Add( new RectangleF( 0.5f, 0, 0.5f, 0.45f ) );
						Grid( L, new RectangleF( 0.5f, 0.45f, 0.5f, 0.55f ), m - 1 );
						break;
					}
				default: // Grid
					Grid( L, full, n );
					break;
			}
			return L;
		}

		/* cell in pixels: half the spacing on every inner side, nothing on the outer sides (the border handles those) */
		public static RectangleF Cell( RectangleF r, RectangleF content, float gap )
		{
			const float e = 0.001f;
			float x0 = content.X + r.X * content.Width + ( r.X > e ? gap / 2 : 0 );
			float y0 = content.Y + r.Y * content.Height + ( r.Y > e ? gap / 2 : 0 );
			float x1 = content.X + r.Right * content.Width - ( r.Right < 1 - e ? gap / 2 : 0 );
			float y1 = content.Y + r.Bottom * content.Height - ( r.Bottom < 1 - e ? gap / 2 : 0 );
			return RectangleF.FromLTRB( x0, y0, x1, y1 );
		}

		public static GraphicsPath Round( RectangleF r, float rad )
		{
			GraphicsPath p = new GraphicsPath();
			rad = Math.Min( rad, Math.Min( r.Width, r.Height ) / 2 );
			if ( rad < 0.5f ) { p.AddRectangle( r ); return p; }
			float d = rad * 2;
			p.AddArc( r.X, r.Y, d, d, 180, 90 );
			p.AddArc( r.Right - d, r.Y, d, d, 270, 90 );
			p.AddArc( r.Right - d, r.Bottom - d, d, d, 0, 90 );
			p.AddArc( r.X, r.Bottom - d, d, d, 90, 90 );
			p.CloseFigure();
			return p;
		}

		/* the picture into its cell: filled (cropped from the centre) or whole; rounded corners with smooth edges */
		static void Put( Graphics g, Bitmap img, RectangleF cell, bool fit, float rad )
		{
			RectangleF dst = cell, src = new RectangleF( 0, 0, img.Width, img.Height );
			if ( fit )
			{
				float k = Math.Min( cell.Width / img.Width, cell.Height / img.Height );
				float w = img.Width * k, h = img.Height * k;
				dst = new RectangleF( cell.X + ( cell.Width - w ) / 2, cell.Y + ( cell.Height - h ) / 2, w, h );
			}
			else
			{
				float ca = cell.Width / cell.Height, ia = img.Width / (float) img.Height;
				if ( ia > ca ) { float sw = img.Height * ca; src = new RectangleF( ( img.Width - sw ) / 2, 0, sw, img.Height ); }
				else { float sh = img.Width / ca; src = new RectangleF( 0, ( img.Height - sh ) / 2, img.Width, sh ); }
			}
			if ( rad < 0.5f )
			{
				g.DrawImage( img, dst, src, GraphicsUnit.Pixel );
				return;
			}
			int w2 = Math.Max( 1, (int) Math.Ceiling( dst.Width ) ), h2 = Math.Max( 1, (int) Math.Ceiling( dst.Height ) );
			using ( Bitmap piece = new Bitmap( w2, h2, PixelFormat.Format32bppArgb ) )
			{
				using ( Graphics pg = Graphics.FromImage( piece ) )
				{
					Marker.Quality( pg );
					pg.DrawImage( img, new RectangleF( 0, 0, w2, h2 ), src, GraphicsUnit.Pixel );
				}
				using ( TextureBrush tb = new TextureBrush( piece, WrapMode.Clamp ) )
				using ( GraphicsPath p = Round( new RectangleF( dst.X, dst.Y, w2, h2 ), rad ) )
				{
					tb.TranslateTransform( dst.X, dst.Y );
					g.FillPath( tb, p );
				}
			}
		}

		public static Bitmap Render( IList<Bitmap> imgs, Settings s, int width, Bitmap mark )
		{
			Size cs = Canvas( s, width );
			Bitmap canvas = new Bitmap( cs.Width, cs.Height, PixelFormat.Format32bppArgb );
			float unit = cs.Width / 1000f;
			float gap = s.CSpacing * unit, outer = s.COuter * unit, rad = s.CRadius * unit;
			using ( Graphics g = Graphics.FromImage( canvas ) )
			{
				Marker.Quality( g );
				g.Clear( Marker.Hex( s.CBg, Color.Black ) );
				RectangleF content = new RectangleF( outer, outer, cs.Width - 2 * outer, cs.Height - 2 * outer );
				List<RectangleF> cells = Layout( s.CTemplate, Math.Min( Max, imgs.Count ) );
				for ( int i = 0; i < imgs.Count && i < cells.Count; i++ )
				{
					RectangleF cell = Cell( cells[i], content, gap );
					if ( cell.Width >= 2 && cell.Height >= 2 ) Put( g, imgs[i], cell, s.CFit, rad );
				}
			}
			if ( s.CMark ) Marker.Draw( canvas, mark, s );
			return canvas;
		}
	}

	/* ---------- the window ---------- */
	partial class MainForm : Form // the RENAME tab is in Rename.cs
	{
		/* dark slate blue, not black; pink and purple for what matters */
		static readonly Color BG = Color.FromArgb( 17, 21, 31 ), PANEL = Color.FromArgb( 23, 28, 41 ), FIELD = Color.FromArgb( 13, 17, 26 ),
			HEAD = Color.FromArgb( 12, 15, 23 ), LINE = Color.FromArgb( 50, 58, 78 ), TEXT = Color.FromArgb( 232, 236, 244 ),
			MUTED = Color.FromArgb( 146, 158, 180 ), PINK = Color.FromArgb( 255, 51, 102 ), GREEN = Color.FromArgb( 74, 222, 128 ),
			AMBER = Color.FromArgb( 255, 176, 32 ), BTN = Color.FromArgb( 40, 48, 68 ), CELL = Color.FromArgb( 64, 74, 96 ),
			CARD = Color.FromArgb( 30, 36, 52 ), PURPLE = Color.FromArgb( 150, 70, 230 );
		const int SIDE_W = 300; // the pictures list and the folders panel: the same width, so nothing jumps between the tabs

		readonly Settings S;
		readonly List<string> files = new List<string>();
		readonly Dictionary<string, Bitmap> thumbs = new Dictionary<string, Bitmap>(); // small copies for the collage preview
		Bitmap mark;
		Bitmap previewBase; // selected picture, scaled down, without the mark
		readonly Timer redraw = new Timer();
		readonly BackgroundWorker worker = new BackgroundWorker();
		string lastOut = "";
		bool loading = true; // no redraw/save while the controls are being filled

		ListBox list;
		Label count, info, lSize, lOpacity, lMargin, lAngle, lQuality, lOutW, lOut, lOut2, status, status2, markName;
		Label lSpacing, lOuter, lRadius, lUsing;
		PictureBox preview, markBox;
		TrackBar tSize, tOpacity, tMargin, tAngle, tQuality, tOutW, tSpacing, tOuter, tRadius;
		ComboBox cPos, cFont, cFormat, cWidth, cType;
		TextBox tText;
		CheckBox kBold, kItalic, kOutline, kShadow, kFit, kMark;
		Button bModeImage, bModeText, bColor, bOutColor, bApply, bOpen, bOpen2, bBg, bSaveCollage;
		SlimBar prog;
		Panel nav, pageBar, statusBar, left, center, secImage, secText;
		NavButton navWm, navCol, navRen;
		Label pageTitle, pageSub;
		FlowLayoutPanel toolWm, toolCol, toolRen;
		FlowLayoutPanel right, rightCol, rightRen;
		readonly List<Panel> tiles = new List<Panel>();
		public MainForm( string[] args )
		{
			S = Settings.Load();
			Text = "ComicsUncut Watermark, Collage & Rename";
			try { Icon = Icon.ExtractAssociatedIcon( Application.ExecutablePath ); } catch { }
			BackColor = BG;
			ForeColor = TEXT;
			Font = new Font( "Segoe UI", 9.75f );
			MinimumSize = new Size( 1240, 760 );
			PlaceWindow();
			AllowDrop = true;
			DragEnter += OnDragEnter;
			DragDrop += OnDragDrop;

			BuildShell();
			BuildLeft();
			BuildRight();
			BuildCollagePanel();
			BuildRenamePanel();
			BuildCenter();
			BuildRenameCenter();
			/* docking order: status bar (full width, bottom), side menu (full height), page header, then the panels */
			center.BringToFront();
			pageBar.SendToBack();
			nav.SendToBack();
			statusBar.SendToBack();
			center.Controls.Remove( info ); // the summary line lives in the status bar
			info.Dock = DockStyle.Fill;
			info.TextAlign = ContentAlignment.MiddleLeft;
			info.Padding = new Padding( 14, 0, 0, 0 );
			statusBar.Controls.Add( info );
			info.BringToFront();

			redraw.Interval = 90;
			redraw.Tick += delegate { redraw.Stop(); RenderPreview(); };
			worker.WorkerReportsProgress = true;
			worker.DoWork += Work;
			worker.ProgressChanged += delegate( object o, ProgressChangedEventArgs e ) { prog.Value = Math.Min( prog.Maximum, e.ProgressPercentage ); status.Text = (string) e.UserState; };
			worker.RunWorkerCompleted += WorkDone;

			loading = false;
			SetMode( S.Mode );
			LoadMark();
			SetTab( S.AppMode );
			AddPaths( args );
			FormClosing += delegate { RememberWindow(); SaveSettings(); };
		}

		/* the window opens where and as big as it was left (maximized too); the first time: big, centred */
		void PlaceWindow()
		{
			Rectangle work = Screen.PrimaryScreen.WorkingArea;
			Rectangle saved = new Rectangle( S.WinX, S.WinY, S.WinW, S.WinH );
			bool onScreen = S.WinW >= MinimumSize.Width && S.WinH >= MinimumSize.Height && Screen.AllScreens.Any( s => s.WorkingArea.IntersectsWith( saved ) );
			StartPosition = FormStartPosition.Manual;
			if ( onScreen ) Bounds = saved;
			else
			{
				Size sz = new Size( Math.Min( 1500, work.Width - 40 ), Math.Min( 940, work.Height - 40 ) );
				Bounds = new Rectangle( work.X + ( work.Width - sz.Width ) / 2, work.Y + ( work.Height - sz.Height ) / 2, sz.Width, sz.Height );
			}
			if ( S.WinMax || ( !onScreen && ( work.Width < 1500 || work.Height < 940 ) ) ) WindowState = FormWindowState.Maximized;
		}

		void RememberWindow()
		{
			Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
			S.WinMax = WindowState == FormWindowState.Maximized;
			if ( WindowState != FormWindowState.Minimized || b.Width > 0 ) { S.WinX = b.X; S.WinY = b.Y; S.WinW = b.Width; S.WinH = b.Height; }
		}

		bool IsCollage { get { return S.AppMode == "collage"; } }
		bool IsRename { get { return S.AppMode == "rename"; } }

		/* ---------- small builders ---------- */
		Label MakeLabel( Control parent, string text, int x, int y, Color color, float size, bool bold )
		{
			Label l = new Label();
			l.Text = text;
			l.AutoSize = true;
			l.Location = new Point( x, y );
			l.ForeColor = color;
			l.BackColor = Color.Transparent;
			l.Font = new Font( "Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular );
			parent.Controls.Add( l );
			return l;
		}

		Button MakeButton( Control parent, string text, int x, int y, int w, int h, bool primary )
		{
			Button b = new RoundButton();
			b.Text = text;
			b.Location = new Point( x, y );
			b.Size = new Size( w, h );
			b.FlatStyle = FlatStyle.Flat;
			b.Cursor = Cursors.Hand;
			b.ForeColor = Color.White;
			b.BackColor = primary ? PINK : BTN;
			b.FlatAppearance.BorderColor = primary ? PINK : LINE;
			b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb( 255, 82, 123 ) : Color.FromArgb( 56, 66, 92 );
			b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb( 224, 30, 82 ) : Color.FromArgb( 70, 80, 110 );
			b.Font = new Font( "Segoe UI Semibold", primary ? 11f : 9.5f );
			parent.Controls.Add( b );
			return b;
		}

		CheckBox MakeCheck( Control parent, string text, int x, int y, bool value )
		{
			CheckBox k = new CheckBox();
			k.Text = text;
			k.Location = new Point( x, y );
			k.AutoSize = true;
			k.ForeColor = TEXT;
			k.Checked = value;
			k.CheckedChanged += delegate { QueueRedraw(); };
			parent.Controls.Add( k );
			return k;
		}

		TrackBar MakeTrack( Control parent, int y, int min, int max, int val )
		{
			TrackBar t = new TrackBar();
			t.Minimum = min;
			t.Maximum = max;
			t.Value = Math.Max( min, Math.Min( max, val ) );
			t.TickStyle = TickStyle.None;
			t.Location = new Point( 6, y );
			t.Width = 268;
			t.BackColor = parent.BackColor; // the card's colour
			t.ValueChanged += delegate { SyncLabels(); QueueRedraw(); };
			parent.Controls.Add( t );
			return t;
		}

		ComboBox MakeCombo( Control parent, int x, int y, int w, object[] items, object selected )
		{
			ComboBox c = new ComboBox();
			c.DropDownStyle = ComboBoxStyle.DropDownList;
			c.FlatStyle = FlatStyle.Flat;
			c.BackColor = FIELD;
			c.ForeColor = TEXT;
			c.Location = new Point( x, y );
			c.Width = w;
			c.Items.AddRange( items );
			c.SelectedItem = c.Items.Contains( selected ) ? selected : items[0];
			c.SelectedIndexChanged += delegate { SyncLabels(); QueueRedraw(); RepaintTiles(); };
			parent.Controls.Add( c );
			return c;
		}

		TextBox MakeBox( Control parent, int x, int y, int w, string text )
		{
			TextBox t = new TextBox();
			t.Location = new Point( x, y );
			t.Width = w;
			t.BackColor = FIELD;
			t.ForeColor = TEXT;
			t.BorderStyle = BorderStyle.FixedSingle;
			t.Text = text;
			t.TextChanged += delegate { QueueRedraw(); };
			parent.Controls.Add( t );
			return t;
		}

		NumericUpDown MakeNum( Control parent, int x, int y, int w, int min, int max, int val )
		{
			NumericUpDown n = new NumericUpDown();
			n.Location = new Point( x, y );
			n.Width = w;
			n.Minimum = min;
			n.Maximum = max;
			n.Value = Math.Max( min, Math.Min( max, val ) );
			n.BackColor = FIELD;
			n.ForeColor = TEXT;
			n.BorderStyle = BorderStyle.FixedSingle;
			n.ValueChanged += delegate { QueueRedraw(); };
			parent.Controls.Add( n );
			return n;
		}

		/* one block of a settings column */
		Panel Section( FlowLayoutPanel column, int height )
		{
			RoundPanel p = new RoundPanel(); // a rounded card
			p.Size = new Size( 280, height );
			p.Margin = new Padding( 0, 0, 0, 6 );
			p.Fill = p.BackColor = CARD;
			column.Controls.Add( p );
			return p;
		}

		TrackBar Slider( FlowLayoutPanel column, out Label label, int min, int max, int val )
		{
			Panel p = Section( column, 62 );
			label = MakeLabel( p, "", 8, 4, MUTED, 9f, true );
			return MakeTrack( p, 24, min, max, val );
		}

		FlowLayoutPanel Column()
		{
			FlowLayoutPanel c = new FlowLayoutPanel();
			c.Dock = DockStyle.Right;
			c.Width = 312;
			c.BackColor = PANEL;
			c.FlowDirection = FlowDirection.TopDown;
			c.WrapContents = false;
			c.AutoScroll = true;
			c.Padding = new Padding( 8, 10, 0, 10 );
			return c;
		}

		void PaintSwatch( Button b, Color c )
		{
			b.BackColor = c;
			b.ForeColor = c.GetBrightness() > 0.6f ? Color.Black : Color.White;
			b.FlatAppearance.MouseOverBackColor = c;
		}

		void StyleToggle( Button b, bool on )
		{
			b.BackColor = on ? PINK : BTN;
			b.FlatAppearance.BorderColor = on ? PINK : LINE;
			b.FlatAppearance.MouseOverBackColor = on ? Color.FromArgb( 255, 82, 123 ) : Color.FromArgb( 45, 30, 40 );
		}

		/* ---------- layout ---------- */
		/* the frame of the app: side menu, page header with its toolbar, status bar */
		void BuildShell()
		{
			/* side menu: logo, the three tools, a hint at the bottom */
			nav = new Panel();
			nav.Dock = DockStyle.Left;
			nav.Width = 232;
			nav.Paint += delegate( object o, PaintEventArgs e )
			{
				using ( LinearGradientBrush b = new LinearGradientBrush( nav.ClientRectangle, Color.FromArgb( 46, 22, 66 ), Color.FromArgb( 16, 19, 30 ), 90f ) )
					e.Graphics.FillRectangle( b, nav.ClientRectangle );
				using ( Pen p = new Pen( Color.FromArgb( 60, 255, 255, 255 ) ) ) e.Graphics.DrawLine( p, nav.Width - 1, 0, nav.Width - 1, nav.Height );
			};
			typeof( Control ).GetProperty( "DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).SetValue( nav, true, null );
			nav.Resize += delegate { nav.Invalidate(); };
			Panel logo = new Panel();
			logo.Location = new Point( 18, 22 );
			logo.Size = new Size( 44, 44 );
			logo.BackColor = Color.Transparent;
			logo.Paint += delegate( object o, PaintEventArgs e )
			{
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				using ( GraphicsPath p = Collage.Round( new RectangleF( 0, 0, 43, 43 ), 12 ) )
				using ( LinearGradientBrush b = new LinearGradientBrush( new Rectangle( 0, 0, 44, 44 ), PINK, PURPLE, 45f ) )
					e.Graphics.FillPath( b, p );
				using ( Font f = new Font( "Segoe UI", 15f, FontStyle.Bold ) )
					TextRenderer.DrawText( e.Graphics, "CU", f, new Rectangle( 0, 0, 44, 44 ), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter );
			};
			nav.Controls.Add( logo );
			MakeLabel( nav, "Comics Uncut", 70, 22, Color.White, 13f, true );
			MakeLabel( nav, "Studio  ·  pictures toolkit", 72, 47, Color.FromArgb( 200, 190, 220 ), 8.5f, false );
			MakeLabel( nav, "TOOLS", 22, 98, Color.FromArgb( 170, 160, 200 ), 8f, true );
			navWm = NavItem( Look.Font, "Watermark", "Logo or text on many pictures", 122, "watermark" );
			navCol = NavItem( Look.Grid, "Collage", "Up to 10 pictures on one page", 190, "collage" );
			navRen = NavItem( Look.Rename, "Rename", "Name many files your way", 258, "rename" );
			Label tip = MakeLabel( nav, "Tip: drop pictures or folders\nanywhere in the window.", 22, 0, Color.FromArgb( 170, 160, 200 ), 8.25f, false );
			nav.Resize += delegate { tip.Top = nav.Height - tip.Height - 20; };
			Controls.Add( nav );

			/* page header: title, one line about it, the page's buttons on the right */
			pageBar = new Panel();
			pageBar.Dock = DockStyle.Top;
			pageBar.Height = 78;
			pageBar.BackColor = BG;
			pageBar.Paint += delegate( object o, PaintEventArgs e ) { using ( Pen p = new Pen( LINE ) ) e.Graphics.DrawLine( p, 0, pageBar.Height - 1, pageBar.Width, pageBar.Height - 1 ); };
			pageTitle = MakeLabel( pageBar, "", 24, 12, Color.White, 18f, true );
			pageSub = MakeLabel( pageBar, "", 26, 48, MUTED, 9.25f, false );
			toolWm = Toolbar();
			ToolButton( toolWm, Look.Picture, "Add pictures", false ).Click += delegate { AddFromDialog(); };
			ToolButton( toolWm, Look.FolderOpen, "Add folder", false ).Click += delegate { AddFolderDialog(); };
			ToolButton( toolWm, Look.Delete, "Clear list", false ).Click += delegate { files.Clear(); RefreshList(); };
			ToolButton( toolWm, Look.Play, "Apply watermark", true ).Click += delegate { Start(); };
			toolCol = Toolbar();
			ToolButton( toolCol, Look.Picture, "Add pictures", false ).Click += delegate { AddFromDialog(); };
			ToolButton( toolCol, Look.FolderOpen, "Add folder", false ).Click += delegate { AddFolderDialog(); };
			ToolButton( toolCol, Look.Delete, "Clear list", false ).Click += delegate { files.Clear(); RefreshList(); };
			ToolButton( toolCol, Look.Save, "Save collage", true ).Click += delegate { SaveCollage(); };
			toolRen = Toolbar();
			ToolButton( toolRen, Look.FolderOpen, "Open folder…", false ).Click += delegate { BrowseRenFolder(); };
			ToolButton( toolRen, Look.Refresh, "Refresh", false ).Click += delegate { ReloadFolder(); };
			ToolButton( toolRen, Look.Explorer, "Show in Explorer", false ).Click += delegate { if ( Directory.Exists( renFolder ) ) Process.Start( "explorer.exe", "\"" + renFolder + "\"" ); };
			ToolButton( toolRen, Look.Undo, "Undo", false ).Click += delegate { DoUndo(); };
			ToolButton( toolRen, Look.Rename, "Rename", true ).Click += delegate { DoRename(); };
			pageBar.Resize += delegate { PlaceToolbars(); };
			Controls.Add( pageBar );

			/* status bar */
			statusBar = new Panel();
			statusBar.Dock = DockStyle.Bottom;
			statusBar.Height = 30;
			statusBar.BackColor = HEAD;
			statusBar.Paint += delegate( object o, PaintEventArgs e ) { using ( Pen p = new Pen( LINE ) ) e.Graphics.DrawLine( p, 0, 0, statusBar.Width, 0 ); };
			Label right = MakeLabel( statusBar, "Settings are saved automatically", 0, 7, Color.FromArgb( 110, 122, 146 ), 8.5f, false );
			right.Dock = DockStyle.Right;
			right.Padding = new Padding( 0, 6, 16, 0 );
			Controls.Add( statusBar );
		}

		NavButton NavItem( string glyph, string title, string sub, int y, string tab )
		{
			NavButton b = new NavButton();
			b.Glyph = glyph;
			b.Title = title;
			b.Sub = sub;
			b.Location = new Point( 12, y );
			b.Width = 208;
			b.Click += delegate { SetTab( tab ); };
			nav.Controls.Add( b );
			return b;
		}

		FlowLayoutPanel Toolbar()
		{
			FlowLayoutPanel t = new FlowLayoutPanel();
			t.WrapContents = false;
			t.BackColor = BG;
			t.Padding = Padding.Empty;
			t.Height = 38;
			t.Width = 0;
			t.Visible = false;
			pageBar.Controls.Add( t );
			return t;
		}

		Button ToolButton( FlowLayoutPanel bar, string glyph, string text, bool primary )
		{
			RoundButton b = (RoundButton) MakeButton( bar, text, 0, 0, 100, 38, primary );
			b.Font = new Font( "Segoe UI Semibold", 9.5f );
			b.Glyph = glyph;
			b.FitWidth( 16 );
			b.Margin = new Padding( bar.Controls.Count == 1 ? 0 : 8, 0, 0, 0 );
			bar.Width += b.Width + b.Margin.Left;
			return b;
		}

		/* the page's buttons: right side of the header, in the middle of its height; the subtitle stops before them */
		void PlaceToolbars()
		{
			foreach ( FlowLayoutPanel t in new FlowLayoutPanel[] { toolWm, toolCol, toolRen } )
			{
				t.Left = pageBar.Width - t.Width - 20;
				t.Top = ( pageBar.Height - t.Height ) / 2;
			}
			FlowLayoutPanel shown = IsRename ? toolRen : IsCollage ? toolCol : toolWm;
			pageSub.AutoSize = false;
			pageSub.AutoEllipsis = true;
			pageSub.Width = Math.Max( 120, shown.Left - pageSub.Left - 24 );
			pageSub.Height = 20;
		}

		void BuildLeft()
		{
			left = new Panel();
			left.Dock = DockStyle.Left;
			left.Width = SIDE_W;
			left.BackColor = PANEL;

			MakeLabel( left, "PICTURES", 16, 16, MUTED, 9f, true );
			Button[] tools = {
				MakeButton( left, "Add", 16, 42, 129, 34, false ), MakeButton( left, "Folder", 153, 42, 129, 34, false ),
				MakeButton( left, "Up", 16, 82, 129, 32, false ), MakeButton( left, "Down", 153, 82, 129, 32, false ),
				MakeButton( left, "Remove", 16, 120, 129, 32, false ), MakeButton( left, "Clear", 153, 120, 129, 32, false ) };
			string[] icons = { Look.Picture, Look.FolderOpen, Look.Up, Look.Down, Look.Close, Look.Delete };
			for ( int i = 0; i < tools.Length; i++ ) ( (RoundButton) tools[i] ).Glyph = icons[i];
			tools[0].Click += delegate { AddFromDialog(); };
			tools[1].Click += delegate { AddFolderDialog(); };
			tools[2].Click += delegate { MoveSelected( -1 ); };
			tools[3].Click += delegate { MoveSelected( 1 ); };
			tools[4].Click += delegate { RemoveSelected(); };
			tools[5].Click += delegate { files.Clear(); RefreshList(); };

			list = new ListBox();
			list.Location = new Point( 16, 164 );
			list.Size = new Size( 266, 520 );
			list.BackColor = FIELD;
			list.ForeColor = TEXT;
			list.BorderStyle = BorderStyle.None;
			list.IntegralHeight = false;
			list.SelectionMode = SelectionMode.MultiExtended;
			list.DrawMode = DrawMode.OwnerDrawFixed;
			list.ItemHeight = 26;
			list.DrawItem += DrawListItem;
			list.SelectedIndexChanged += delegate { if ( IsCollage ) { QueueRedraw(); RepaintTiles(); } else LoadPreviewBase(); };
			list.KeyDown += delegate( object o, KeyEventArgs e ) { if ( e.KeyCode == Keys.Delete ) RemoveSelected(); };
			list.AllowDrop = true;
			list.DragEnter += OnDragEnter;
			list.DragDrop += OnDragDrop;
			left.Controls.Add( list );

			count = MakeLabel( left, "No pictures yet", 16, 0, MUTED, 9f, false );
			left.Resize += delegate { count.Top = left.Height - 30; list.Height = left.Height - 164 - 40; };
			Controls.Add( left );
		}

		/* WATERMARK settings column */
		void BuildRight()
		{
			right = Column();

			/* mark kind: image or text */
			Panel pm = Section( right, 76 );
			MakeLabel( pm, "WATERMARK", 8, 4, MUTED, 9f, true );
			bModeImage = MakeButton( pm, "IMAGE", 8, 30, 132, 34, false );
			bModeText = MakeButton( pm, "TEXT", 142, 30, 132, 34, false );
			bModeImage.Click += delegate { SetMode( "image" ); };
			bModeText.Click += delegate { SetMode( "text" ); };

			/* image mark */
			secImage = Section( right, 96 );
			markBox = new PictureBox();
			markBox.Location = new Point( 8, 4 );
			markBox.Size = new Size( 122, 58 );
			markBox.SizeMode = PictureBoxSizeMode.Zoom;
			markBox.BackColor = FIELD;
			secImage.Controls.Add( markBox );
			MakeButton( secImage, "Choose picture…", 136, 4, 138, 27, false ).Click += delegate { ChooseMark(); };
			MakeButton( secImage, "Remove", 136, 35, 138, 27, false ).Click += delegate { S.Mark = ""; LoadMark(); };
			markBox.Cursor = Cursors.Hand;
			markBox.Click += delegate { ChooseMark(); };
			markName = MakeLabel( secImage, "", 8, 68, MUTED, 8.5f, false );

			/* text mark */
			secText = Section( right, 268 );
			MakeLabel( secText, "Text", 8, 2, MUTED, 8.5f, false );
			tText = new TextBox();
			tText.Location = new Point( 8, 20 );
			tText.Width = 266;
			tText.BackColor = FIELD;
			tText.ForeColor = TEXT;
			tText.BorderStyle = BorderStyle.FixedSingle;
			tText.Font = new Font( "Segoe UI", 11f );
			tText.Text = S.Text;
			tText.TextChanged += delegate { QueueRedraw(); };
			secText.Controls.Add( tText );

			MakeLabel( secText, "Font", 8, 56, MUTED, 8.5f, false );
			cFont = new ComboBox();
			cFont.DropDownStyle = ComboBoxStyle.DropDownList;
			cFont.FlatStyle = FlatStyle.Flat;
			cFont.BackColor = FIELD;
			cFont.ForeColor = TEXT;
			cFont.Location = new Point( 8, 74 );
			cFont.Width = 266;
			cFont.DrawMode = DrawMode.OwnerDrawFixed; // each font name written in its own font
			cFont.ItemHeight = 22;
			cFont.MaxDropDownItems = 16;
			cFont.DrawItem += DrawFontItem;
			foreach ( FontFamily f in FontFamily.Families ) cFont.Items.Add( f.Name );
			int fi = cFont.Items.IndexOf( S.FontName );
			if ( fi < 0 ) fi = cFont.Items.IndexOf( "Arial Black" );
			if ( fi < 0 ) fi = cFont.Items.IndexOf( "Arial" );
			cFont.SelectedIndex = Math.Max( 0, fi );
			cFont.SelectedIndexChanged += delegate { QueueRedraw(); };
			secText.Controls.Add( cFont );

			kBold = MakeCheck( secText, "Bold", 8, 108, S.Bold );
			kItalic = MakeCheck( secText, "Italic", 70, 108, S.Italic );
			bColor = MakeButton( secText, "Color", 148, 104, 126, 28, false );
			PaintSwatch( bColor, Marker.Hex( S.TextColor, Color.White ) );
			bColor.Click += delegate { PickColor( 0 ); };

			kOutline = MakeCheck( secText, "Outline", 8, 146, S.Outline );
			bOutColor = MakeButton( secText, "Outline color", 148, 142, 126, 28, false );
			PaintSwatch( bOutColor, Marker.Hex( S.OutlineColor, Color.Black ) );
			bOutColor.Click += delegate { PickColor( 1 ); };
			lOutW = MakeLabel( secText, "", 8, 178, MUTED, 8.5f, false );
			tOutW = MakeTrack( secText, 196, 1, 20, S.OutlineWidth );
			kShadow = MakeCheck( secText, "Shadow", 8, 238, S.Shadow );

			/* shared settings */
			Panel pp = Section( right, 66 );
			MakeLabel( pp, "POSITION", 8, 4, MUTED, 9f, true );
			cPos = MakeCombo( pp, 8, 28, 266, new object[] { "Bottom right", "Bottom left", "Bottom center", "Top right", "Top left", "Top center", "Center" }, S.Position );

			tSize = Slider( right, out lSize, 5, 80, S.Size );
			tOpacity = Slider( right, out lOpacity, 10, 100, S.Opacity );
			tMargin = Slider( right, out lMargin, 0, 20, S.Margin );
			tAngle = Slider( right, out lAngle, -45, 45, S.Angle );
			tQuality = Slider( right, out lQuality, 70, 100, S.Quality );

			lOut = SaveToSection( right );

			Panel pa = Section( right, 150 );
			bApply = MakeButton( pa, "APPLY WATERMARK", 8, 6, 266, 48, true );
			bApply.Click += delegate { Start(); };
			prog = new SlimBar();
			prog.Location = new Point( 8, 62 );
			prog.Size = new Size( 266, 6 );
			pa.Controls.Add( prog );
			status = MakeLabel( pa, "", 8, 76, MUTED, 8.75f, false );
			status.AutoSize = false;
			status.Size = new Size( 266, 34 );
			bOpen = MakeButton( pa, "Open the folder", 8, 112, 266, 30, false );
			bOpen.Visible = false;
			bOpen.Click += delegate { OpenOut(); };

			Controls.Add( right );
		}

		/* SAVE TO block (both columns use the same folder) */
		Label SaveToSection( FlowLayoutPanel column )
		{
			Panel ps = Section( column, 112 );
			MakeLabel( ps, "SAVE TO", 8, 4, MUTED, 9f, true );
			Label l = MakeLabel( ps, "", 8, 28, TEXT, 9f, false );
			l.AutoSize = false;
			l.Size = new Size( 266, 38 );
			l.BackColor = FIELD;
			l.Padding = new Padding( 6, 3, 6, 3 );
			l.Cursor = Cursors.Hand;
			l.Click += delegate { ChooseOutFolder(); };
			MakeButton( ps, "Choose folder…", 8, 72, 266, 30, false ).Click += delegate { ChooseOutFolder(); };
			return l;
		}

		/* COLLAGE settings column */
		void BuildCollagePanel()
		{
			rightCol = Column();

			Panel pt = Section( rightCol, 58 );
			MakeLabel( pt, "COLLAGE", 8, 4, MUTED, 9f, true );
			lUsing = MakeLabel( pt, "", 8, 24, TEXT, 8.75f, false );
			lUsing.AutoSize = false;
			lUsing.Size = new Size( 266, 32 );

			/* 10 template tiles, two per row, each drawing its layout for the current number of pictures */
			Panel pg = Section( rightCol, 5 * 92 + 26 );
			MakeLabel( pg, "TEMPLATE", 8, 2, MUTED, 9f, true );
			for ( int i = 0; i < Collage.Names.Length; i++ )
			{
				Panel tile = new Panel();
				tile.Size = new Size( 130, 86 );
				tile.Location = new Point( 8 + ( i % 2 ) * 136, 24 + ( i / 2 ) * 92 );
				tile.BackColor = FIELD;
				tile.Cursor = Cursors.Hand;
				tile.Tag = i;
				tile.Paint += DrawTile;
				tile.Click += delegate( object o, EventArgs e ) { S.CTemplate = (int) ( (Panel) o ).Tag; RepaintTiles(); QueueRedraw(); };
				pg.Controls.Add( tile );
				tiles.Add( tile );
			}

			Panel pf = Section( rightCol, 66 );
			MakeLabel( pf, "FORMAT", 8, 4, MUTED, 9f, true );
			MakeLabel( pf, "WIDTH", 160, 4, MUTED, 9f, true );
			cFormat = MakeCombo( pf, 8, 28, 146, Collage.Formats.Cast<object>().ToArray(), S.CFormat );
			cWidth = MakeCombo( pf, 160, 28, 114, Collage.Widths.Select( w => (object) ( w + " px" ) ).ToArray(), S.CWidth + " px" );

			tSpacing = Slider( rightCol, out lSpacing, 0, 60, S.CSpacing );
			tOuter = Slider( rightCol, out lOuter, 0, 80, S.COuter );
			tRadius = Slider( rightCol, out lRadius, 0, 60, S.CRadius );

			Panel po = Section( rightCol, 118 );
			MakeLabel( po, "Background", 8, 8, MUTED, 8.5f, false );
			bBg = MakeButton( po, "Background color", 110, 2, 164, 28, false );
			PaintSwatch( bBg, Marker.Hex( S.CBg, Color.Black ) );
			bBg.Click += delegate { PickColor( 2 ); };
			kFit = MakeCheck( po, "Whole pictures (no cropping)", 8, 38, S.CFit );
			kMark = MakeCheck( po, "Add the watermark (Watermark tab settings)", 8, 62, S.CMark );
			MakeLabel( po, "Save as", 8, 92, MUTED, 8.5f, false );
			cType = MakeCombo( po, 70, 88, 80, new object[] { "JPG", "PNG" }, S.CType );

			lOut2 = SaveToSection( rightCol );

			Panel pa = Section( rightCol, 130 );
			bSaveCollage = MakeButton( pa, "SAVE COLLAGE", 8, 6, 266, 48, true );
			bSaveCollage.Click += delegate { SaveCollage(); };
			status2 = MakeLabel( pa, "", 8, 60, MUTED, 8.75f, false );
			status2.AutoSize = false;
			status2.Size = new Size( 266, 30 );
			bOpen2 = MakeButton( pa, "Open the folder", 8, 92, 266, 30, false );
			bOpen2.Visible = false;
			bOpen2.Click += delegate { OpenOut(); };

			SyncLabels();
			Controls.Add( rightCol );
		}

		void BuildCenter()
		{
			center = new Panel();
			center.Dock = DockStyle.Fill;
			center.BackColor = BG;
			center.Padding = new Padding( 18 );

			preview = new PictureBox();
			preview.Dock = DockStyle.Fill;
			preview.SizeMode = PictureBoxSizeMode.Zoom;
			preview.BackColor = BG;
			preview.Paint += delegate( object o, PaintEventArgs e )
			{
				if ( preview.Image != null ) return;
				using ( StringFormat sf = new StringFormat() )
				using ( Brush br = new SolidBrush( MUTED ) )
				using ( Font f = new Font( "Segoe UI", 12f ) )
				{
					sf.Alignment = StringAlignment.Center;
					sf.LineAlignment = StringAlignment.Center;
					e.Graphics.DrawString( "Add pictures with  + Pictures  /  + Folder\nor drop them here", f, br, preview.ClientRectangle, sf );
				}
			};
			preview.Resize += delegate { QueueRedraw(); };

			info = new Label();
			info.Dock = DockStyle.Bottom;
			info.Height = 30;
			info.TextAlign = ContentAlignment.MiddleCenter;
			info.ForeColor = MUTED;

			center.Controls.Add( preview );
			center.Controls.Add( info );
			Controls.Add( center );
		}

		void DrawListItem( object sender, DrawItemEventArgs e )
		{
			if ( e.Index < 0 || e.Index >= files.Count ) return;
			bool sel = ( e.State & DrawItemState.Selected ) != 0;
			using ( Brush bg = new SolidBrush( sel ? Color.FromArgb( 60, 255, 51, 102 ) : FIELD ) )
				e.Graphics.FillRectangle( bg, e.Bounds );
			if ( sel )
				using ( Brush bar = new SolidBrush( PINK ) )
					e.Graphics.FillRectangle( bar, e.Bounds.X, e.Bounds.Y + 4, 3, e.Bounds.Height - 8 );
			Rectangle r = new Rectangle( e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height );
			TextRenderer.DrawText( e.Graphics, ( e.Index + 1 ) + ".  " + Path.GetFileName( files[e.Index] ), Font, r, sel ? Color.White : TEXT,
				TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix );
		}

		readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();
		void DrawFontItem( object sender, DrawItemEventArgs e )
		{
			if ( e.Index < 0 ) return;
			string name = cFont.Items[e.Index].ToString();
			bool sel = ( e.State & DrawItemState.Selected ) != 0;
			using ( Brush bg = new SolidBrush( sel ? Color.FromArgb( 90, 255, 51, 102 ) : FIELD ) )
				e.Graphics.FillRectangle( bg, e.Bounds );
			Font f;
			if ( !fontCache.TryGetValue( name, out f ) )
			{
				try { f = new Font( name, 11f ); } catch { f = Font; }
				fontCache[name] = f;
			}
			TextRenderer.DrawText( e.Graphics, name, ( e.State & DrawItemState.ComboBoxEdit ) != 0 ? Font : f, e.Bounds, TEXT,
				TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Left );
		}

		/* template tile: its layout drawn in the chosen format, for the pictures the collage would use */
		void DrawTile( object sender, PaintEventArgs e )
		{
			Panel tile = (Panel) sender;
			int t = (int) tile.Tag;
			bool sel = t == S.CTemplate;
			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			int n = Math.Max( 1, Math.Min( Collage.Max, CollageFiles().Count ) );
			if ( CollageFiles().Count == 0 ) n = 6; // example while the list is empty

			SizeF ratio = Collage.Ratio( cFormat == null ? S.CFormat : cFormat.SelectedItem.ToString() );
			RectangleF area = new RectangleF( 8, 6, tile.Width - 16, tile.Height - 28 );
			float k = Math.Min( area.Width / ratio.Width, area.Height / ratio.Height );
			RectangleF box = new RectangleF( area.X + ( area.Width - ratio.Width * k ) / 2, area.Y + ( area.Height - ratio.Height * k ) / 2, ratio.Width * k, ratio.Height * k );
			using ( Brush cb = new SolidBrush( sel ? PINK : CELL ) )
			using ( Brush cb0 = new SolidBrush( sel ? Color.FromArgb( 255, 120, 155 ) : Color.FromArgb( 84, 94, 108 ) ) )
			{
				List<RectangleF> cells = Collage.Layout( t, n );
				for ( int i = 0; i < cells.Count; i++ )
				{
					RectangleF c = Collage.Cell( cells[i], box, 3f );
					g.FillRectangle( i == 0 ? cb0 : cb, c );
				}
			}
			TextRenderer.DrawText( g, Collage.Names[t], Font, new Rectangle( 0, tile.Height - 22, tile.Width, 20 ), sel ? Color.White : MUTED, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter );
			if ( sel )
				using ( Pen p = new Pen( PINK, 2f ) )
					g.DrawRectangle( p, 1, 1, tile.Width - 3, tile.Height - 3 );
		}

		void RepaintTiles()
		{
			foreach ( Panel t in tiles ) t.Invalidate();
		}

		/* ---------- state ---------- */
		void SetTab( string tab )
		{
			S.AppMode = tab == "collage" || tab == "rename" ? tab : "watermark";
			bool wm = !IsCollage && !IsRename;
			SuspendLayout(); // the panels change in one go: the same widths in every tab, nothing jumps
			right.Visible = wm;
			rightCol.Visible = IsCollage;
			renRight.Visible = IsRename;
			left.Visible = !IsRename;    // the rename tab has its own folders panel and file list
			renLeft.Visible = IsRename;
			preview.Visible = !IsRename;
			renWrap.Visible = IsRename;
			toolWm.Visible = wm;
			toolCol.Visible = IsCollage;
			toolRen.Visible = IsRename;
			navWm.Selected = wm;
			navCol.Selected = IsCollage;
			navRen.Selected = IsRename;
			pageTitle.Text = wm ? "Watermark" : IsCollage ? "Collage" : "Rename";
			pageSub.Text = wm ? "Put your logo or a text on many pictures at once — the originals are never changed"
				: IsCollage ? "Up to 10 pictures on one page, 10 templates — pick the pictures in the list, the first one gets the big spot"
				: "Rename or copy many files at once — with a pattern, or double-click a file and give it any name";
			PlaceToolbars();
			ResumeLayout( true );
			if ( IsRename && renFolder == "" ) OpenFolder( Directory.Exists( S.RFolder ) ? S.RFolder : Environment.GetFolderPath( Environment.SpecialFolder.Desktop ) );
			if ( IsCollage ) { RepaintTiles(); QueueRedraw(); }
			else if ( IsRename ) QueueRedraw();
			else LoadPreviewBase();
		}

		void SetMode( string mode )
		{
			S.Mode = mode == "text" ? "text" : "image";
			bool text = S.Mode == "text";
			secImage.Visible = !text;
			secText.Visible = text;
			StyleToggle( bModeImage, !text );
			StyleToggle( bModeText, text );
			QueueRedraw();
		}

		/* 0 = text colour, 1 = outline colour, 2 = collage background */
		void PickColor( int which )
		{
			using ( ColorDialog d = new ColorDialog() )
			{
				d.FullOpen = true;
				d.Color = which == 0 ? Marker.Hex( S.TextColor, Color.White ) : which == 1 ? Marker.Hex( S.OutlineColor, Color.Black ) : Marker.Hex( S.CBg, Color.Black );
				if ( d.ShowDialog( this ) != DialogResult.OK ) return;
				if ( which == 0 ) { S.TextColor = Marker.ToHex( d.Color ); PaintSwatch( bColor, d.Color ); }
				else if ( which == 1 ) { S.OutlineColor = Marker.ToHex( d.Color ); PaintSwatch( bOutColor, d.Color ); kOutline.Checked = true; }
				else { S.CBg = Marker.ToHex( d.Color ); PaintSwatch( bBg, d.Color ); }
				QueueRedraw();
			}
		}

		void ReadSettings()
		{
			S.Size = tSize.Value;
			S.Opacity = tOpacity.Value;
			S.Margin = tMargin.Value;
			S.Angle = tAngle.Value;
			S.Quality = tQuality.Value;
			S.Position = cPos.SelectedItem == null ? "Bottom right" : cPos.SelectedItem.ToString();
			S.Text = tText.Text;
			S.FontName = cFont.SelectedItem == null ? "Arial" : cFont.SelectedItem.ToString();
			S.Bold = kBold.Checked;
			S.Italic = kItalic.Checked;
			S.Outline = kOutline.Checked;
			S.OutlineWidth = tOutW.Value;
			S.Shadow = kShadow.Checked;
			S.CFormat = cFormat.SelectedItem == null ? "Portrait 4:5" : cFormat.SelectedItem.ToString();
			int w;
			if ( cWidth.SelectedItem != null && int.TryParse( cWidth.SelectedItem.ToString().Replace( " px", "" ), out w ) ) S.CWidth = w;
			S.CSpacing = tSpacing.Value;
			S.COuter = tOuter.Value;
			S.CRadius = tRadius.Value;
			S.CFit = kFit.Checked;
			S.CMark = kMark.Checked;
			S.CType = cType.SelectedItem == null ? "JPG" : cType.SelectedItem.ToString();
			ReadRenameSettings();
		}

		void SaveSettings()
		{
			ReadSettings();
			S.Save();
		}

		void SyncLabels()
		{
			if ( tSize != null && tOpacity != null && tMargin != null && tAngle != null && tQuality != null )
			{
				lSize.Text = "SIZE   " + tSize.Value + "% of the width";
				lOpacity.Text = "OPACITY   " + tOpacity.Value + "%";
				lMargin.Text = "MARGIN   " + ( tMargin.Value / 2.0 ).ToString( "0.0" ) + "%";
				lAngle.Text = "ANGLE   " + tAngle.Value + "°";
				lQuality.Text = "JPG QUALITY   " + tQuality.Value;
			}
			if ( tOutW != null ) lOutW.Text = "Outline thickness   " + tOutW.Value;
			if ( tSpacing != null && tOuter != null && tRadius != null && cWidth != null )
			{
				int w = 2048;
				if ( cWidth.SelectedItem != null ) int.TryParse( cWidth.SelectedItem.ToString().Replace( " px", "" ), out w );
				lSpacing.Text = "SPACING BETWEEN PICTURES   " + (int) Math.Round( tSpacing.Value * w / 1000.0 ) + " px";
				lOuter.Text = "OUTER BORDER   " + (int) Math.Round( tOuter.Value * w / 1000.0 ) + " px";
				lRadius.Text = "ROUNDED CORNERS   " + (int) Math.Round( tRadius.Value * w / 1000.0 ) + " px";
			}
			bool okDir = S.OutFolder != "" && Directory.Exists( S.OutFolder );
			foreach ( Label l in new Label[] { lOut, lOut2 } )
			{
				if ( l == null ) continue;
				l.Text = okDir ? S.OutFolder : "No folder chosen yet — click to choose";
				l.ForeColor = okDir ? TEXT : AMBER;
			}
		}

		void QueueRedraw()
		{
			if ( loading ) return;
			redraw.Stop();
			redraw.Start();
		}

		void LoadMark()
		{
			string path = S.MarkPath();
			Bitmap old = mark;
			mark = null;
			markBox.Image = null;
			if ( old != null ) old.Dispose();
			if ( path == "" )
			{
				markName.Text = "No picture chosen yet (transparent PNG is best)";
				QueueRedraw();
				return;
			}
			try
			{
				mark = Marker.Load( path );
				markBox.Image = mark;
				markName.Text = Path.GetFileName( path ) + "  ·  " + mark.Width + " × " + mark.Height;
			}
			catch ( Exception ex )
			{
				markName.Text = "Cannot open: " + Path.GetFileName( path );
				MessageBox.Show( this, "The watermark picture could not be loaded:\n" + path + "\n\n" + ex.Message, "Watermark", MessageBoxButtons.OK, MessageBoxIcon.Warning );
			}
			QueueRedraw();
		}

		/* ---------- adding pictures ---------- */
		void AddPaths( IEnumerable<string> paths )
		{
			int before = files.Count;
			foreach ( string p in paths )
			{
				if ( Directory.Exists( p ) )
				{
					foreach ( string f in Directory.GetFiles( p ).Where( Marker.IsPicture ).OrderBy( f => f, new NaturalComparer() ) )
						if ( !files.Contains( f, StringComparer.OrdinalIgnoreCase ) ) files.Add( f );
				}
				else if ( File.Exists( p ) && Marker.IsPicture( p ) && !files.Contains( p, StringComparer.OrdinalIgnoreCase ) )
				{
					files.Add( p );
				}
			}
			RefreshList();
			if ( files.Count > before && list.SelectedIndex < 0 && files.Count > 0 && !IsCollage && !IsRename ) list.SelectedIndex = before;
		}

		void AddFromDialog()
		{
			using ( OpenFileDialog d = new OpenFileDialog() )
			{
				d.Title = "Choose the pictures";
				d.Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|All files|*.*";
				d.Multiselect = true;
				if ( Directory.Exists( @"D:\Comics" ) ) d.InitialDirectory = @"D:\Comics";
				if ( d.ShowDialog( this ) == DialogResult.OK ) AddPaths( d.FileNames );
			}
		}

		void AddFolderDialog()
		{
			using ( FolderBrowserDialog d = new FolderBrowserDialog() )
			{
				d.Description = "Choose a folder: every picture in it is added";
				if ( Directory.Exists( @"D:\Comics" ) ) d.SelectedPath = @"D:\Comics";
				if ( d.ShowDialog( this ) == DialogResult.OK ) AddPaths( new string[] { d.SelectedPath } );
			}
		}

		void RemoveSelected()
		{
			List<int> idx = list.SelectedIndices.Cast<int>().OrderByDescending( i => i ).ToList();
			foreach ( int i in idx ) files.RemoveAt( i );
			RefreshList();
		}

		/* one picture up or down (the order is the order in the collage) */
		void MoveSelected( int dir )
		{
			if ( list.SelectedIndices.Count != 1 ) return;
			int i = list.SelectedIndex, j = i + dir;
			if ( j < 0 || j >= files.Count ) return;
			string tmp = files[i];
			files[i] = files[j];
			files[j] = tmp;
			RefreshList();
			list.SelectedIndex = j;
		}

		void RefreshList()
		{
			list.BeginUpdate();
			list.Items.Clear();
			foreach ( string f in files ) list.Items.Add( Path.GetFileName( f ) );
			list.EndUpdate();
			count.Text = files.Count == 0 ? "No pictures yet" : files.Count + ( files.Count == 1 ? " picture" : " pictures" );
			foreach ( string k in thumbs.Keys.Where( k => !files.Contains( k ) ).ToList() ) { thumbs[k].Dispose(); thumbs.Remove( k ); }
			if ( IsCollage ) { RepaintTiles(); QueueRedraw(); }
			else if ( IsRename ) QueueRedraw();
			else if ( files.Count == 0 ) LoadPreviewBase();
		}

		void ChooseMark()
		{
			using ( OpenFileDialog d = new OpenFileDialog() )
			{
				d.Title = "Choose the watermark (a transparent PNG works best)";
				d.Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*";
				if ( d.ShowDialog( this ) == DialogResult.OK ) { S.Mark = d.FileName; LoadMark(); }
			}
		}

		/* true when the folder is one the pictures come from (saving there could overwrite an original) */
		bool IsSourceFolder( string dir )
		{
			string d = Path.GetFullPath( dir ).TrimEnd( '\\' );
			foreach ( string f in files )
				if ( string.Equals( Path.GetFullPath( Path.GetDirectoryName( f ) ).TrimEnd( '\\' ), d, StringComparison.OrdinalIgnoreCase ) )
					return true;
			return false;
		}

		bool ChooseOutFolder()
		{
			while ( true )
			{
				using ( FolderBrowserDialog d = new FolderBrowserDialog() )
				{
					d.Description = "Where to save (a different folder from the original pictures)";
					d.ShowNewFolderButton = true;
					if ( Directory.Exists( S.OutFolder ) ) d.SelectedPath = S.OutFolder;
					if ( d.ShowDialog( this ) != DialogResult.OK ) return false;
					if ( IsSourceFolder( d.SelectedPath ) )
					{
						MessageBox.Show( this, "This is the folder the pictures come from.\nChoose another folder (or make a new one), so the originals are not overwritten.", "Choose another folder", MessageBoxButtons.OK, MessageBoxIcon.Warning );
						continue;
					}
					S.OutFolder = d.SelectedPath;
					SyncLabels();
					return true;
				}
			}
		}

		bool EnsureOutFolder()
		{
			if ( S.OutFolder != "" && Directory.Exists( S.OutFolder ) && !IsSourceFolder( S.OutFolder ) ) return true;
			if ( S.OutFolder != "" && Directory.Exists( S.OutFolder ) && IsSourceFolder( S.OutFolder ) )
				MessageBox.Show( this, "The chosen folder is the one some pictures come from.\nChoose another folder, so the originals are not overwritten.", "Choose another folder", MessageBoxButtons.OK, MessageBoxIcon.Warning );
			return ChooseOutFolder();
		}

		void OpenOut()
		{
			if ( Directory.Exists( lastOut ) ) Process.Start( "explorer.exe", "\"" + lastOut + "\"" );
		}

		void OnDragEnter( object sender, DragEventArgs e )
		{
			e.Effect = e.Data.GetDataPresent( DataFormats.FileDrop ) ? DragDropEffects.Copy : DragDropEffects.None;
		}

		void OnDragDrop( object sender, DragEventArgs e )
		{
			string[] p = e.Data.GetData( DataFormats.FileDrop ) as string[];
			if ( p == null ) return;
			if ( IsRename ) RenameDrop( p );
			else AddPaths( p );
		}

		/* ---------- preview ---------- */
		void LoadPreviewBase()
		{
			if ( previewBase != null ) { previewBase.Dispose(); previewBase = null; }
			if ( IsCollage || IsRename ) { QueueRedraw(); return; }
			int i = list.SelectedIndex;
			if ( i < 0 || i >= files.Count ) { SetPreview( null ); info.Text = ""; return; }
			try
			{
				using ( Bitmap full = Marker.Load( files[i] ) )
				{
					double k = Math.Min( 1.0, 1400.0 / Math.Max( full.Width, full.Height ) );
					int w = Math.Max( 1, (int) ( full.Width * k ) ), h = Math.Max( 1, (int) ( full.Height * k ) );
					previewBase = new Bitmap( w, h, PixelFormat.Format32bppArgb );
					using ( Graphics g = Graphics.FromImage( previewBase ) )
					{
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.DrawImage( full, 0, 0, w, h );
					}
					info.Text = Path.GetFileName( files[i] ) + "   ·   " + full.Width + " × " + full.Height + " px   ·   " + ( new FileInfo( files[i] ).Length / 1024 ) + " KB";
				}
			}
			catch ( Exception ex )
			{
				info.Text = "Cannot open " + Path.GetFileName( files[i] ) + ": " + ex.Message;
			}
			RenderPreview();
		}

		/* the collage uses the selected pictures (2 or more), otherwise the first 10 in the list */
		List<string> CollageFiles()
		{
			if ( list != null && list.SelectedIndices.Count >= 2 )
				return list.SelectedIndices.Cast<int>().OrderBy( i => i ).Take( Collage.Max ).Select( i => files[i] ).ToList();
			return files.Take( Collage.Max ).ToList();
		}

		Bitmap ThumbFor( string path )
		{
			Bitmap b;
			if ( !thumbs.TryGetValue( path, out b ) )
			{
				b = Marker.Thumb( path, 900 );
				thumbs[path] = b;
			}
			return b;
		}

		void RenderPreview()
		{
			ReadSettings();
			if ( IsRename ) { UpdateNames(); return; }
			if ( IsCollage )
			{
				List<string> use = CollageFiles();
				bool selected = list.SelectedIndices.Count >= 2;
				lUsing.Text = use.Count == 0 ? "Add 2 to 10 pictures."
					: "Using " + use.Count + ( use.Count == 1 ? " picture" : " pictures" ) + ( selected ? " (the selected ones)" : files.Count > Collage.Max ? " (the first 10; select others to change)" : "" );
				if ( use.Count == 0 ) { SetPreview( null ); info.Text = ""; return; }
				try
				{
					List<Bitmap> imgs = use.Select( ThumbFor ).ToList();
					Bitmap c = Collage.Render( imgs, S, 1000, mark );
					SetPreview( c );
					Size full = Collage.Canvas( S, S.CWidth );
					info.Text = Collage.Names[S.CTemplate] + "   ·   " + use.Count + " pictures   ·   " + full.Width + " × " + full.Height + " px   ·   " + S.CType;
				}
				catch ( Exception ex )
				{
					info.Text = "Cannot build the collage: " + ex.Message;
				}
				return;
			}
			if ( previewBase == null ) { SetPreview( null ); return; }
			Bitmap b = new Bitmap( previewBase );
			Marker.Draw( b, mark, S );
			SetPreview( b );
		}

		void SetPreview( Bitmap b )
		{
			Image old = preview.Image;
			preview.Image = b;
			if ( old != null ) old.Dispose();
			preview.Invalidate();
		}

		/* ---------- collage: save ---------- */
		void SaveCollage()
		{
			ReadSettings();
			List<string> use = CollageFiles();
			if ( use.Count == 0 ) { MessageBox.Show( this, "Add some pictures first.", "Collage", MessageBoxButtons.OK, MessageBoxIcon.Information ); return; }
			if ( S.CMark && S.Mode == "image" && mark == null ) { MessageBox.Show( this, "No watermark picture chosen (Watermark tab). Untick \"Add the watermark\", choose a picture, or switch the watermark to TEXT.", "Collage", MessageBoxButtons.OK, MessageBoxIcon.Information ); return; }
			if ( !EnsureOutFolder() ) return;
			SaveSettings();

			Cursor = Cursors.WaitCursor;
			bSaveCollage.Enabled = false;
			status2.ForeColor = MUTED;
			status2.Text = "Building the collage…";
			Application.DoEvents();
			List<Bitmap> imgs = new List<Bitmap>();
			try
			{
				foreach ( string f in use ) imgs.Add( Marker.Load( f ) );
				using ( Bitmap c = Collage.Render( imgs, S, S.CWidth, mark ) )
				{
					string dst = Marker.FreeName( S.OutFolder, "Collage " + DateTime.Now.ToString( "yyyy-MM-dd HH.mm.ss" ) + ( S.CType == "PNG" ? ".png" : ".jpg" ) );
					Marker.Save( c, dst, S.Quality );
					lastOut = S.OutFolder;
					status2.ForeColor = GREEN;
					status2.Text = "✔ Saved " + Path.GetFileName( dst );
					bOpen2.Visible = true;
				}
			}
			catch ( Exception ex )
			{
				status2.ForeColor = AMBER;
				status2.Text = "Could not save: " + ex.Message;
			}
			finally
			{
				foreach ( Bitmap b in imgs ) b.Dispose();
				Cursor = Cursors.Default;
				bSaveCollage.Enabled = true;
			}
		}

		/* ---------- watermark: apply ---------- */
		class Job { public List<string> Files; public Bitmap Mark; public Settings S; }
		class Result { public int Done; public List<string> Errors = new List<string>(); public string LastDir = ""; }

		void Start()
		{
			if ( worker.IsBusy ) return;
			ReadSettings();
			if ( files.Count == 0 ) { MessageBox.Show( this, "Add some pictures first.", "Watermark", MessageBoxButtons.OK, MessageBoxIcon.Information ); return; }
			if ( S.Mode == "image" && mark == null ) { MessageBox.Show( this, "Choose the watermark picture first (Choose picture…), or switch to TEXT.", "Watermark", MessageBoxButtons.OK, MessageBoxIcon.Information ); return; }
			if ( S.Mode == "text" && S.Text.Trim() == "" ) { MessageBox.Show( this, "Write the watermark text first.", "Watermark", MessageBoxButtons.OK, MessageBoxIcon.Information ); tText.Focus(); return; }
			if ( !EnsureOutFolder() ) return;
			SaveSettings();

			Job job = new Job();
			job.Files = new List<string>( files );
			job.Mark = mark != null ? new Bitmap( mark ) : null;
			job.S = S.Clone();

			bApply.Enabled = false;
			bApply.Text = "WORKING…";
			bOpen.Visible = false;
			prog.Reset();
			prog.Maximum = job.Files.Count;
			status.ForeColor = MUTED;
			worker.RunWorkerAsync( job );
		}

		void Work( object sender, DoWorkEventArgs e )
		{
			Job job = (Job) e.Argument;
			Result res = new Result();
			for ( int i = 0; i < job.Files.Count; i++ )
			{
				string f = job.Files[i];
				worker.ReportProgress( i, ( i + 1 ) + " / " + job.Files.Count + "   " + Path.GetFileName( f ) );
				try
				{
					string dir = job.S.OutFolder;
					Directory.CreateDirectory( dir );
					string dst = Marker.FreeName( dir, Path.GetFileName( f ) ); // never replaces a file that is already there
					if ( string.Equals( Path.GetFullPath( dst ), Path.GetFullPath( f ), StringComparison.OrdinalIgnoreCase ) )
						throw new Exception( "the output folder is the picture's own folder (the original would be overwritten)" );
					using ( Bitmap b = Marker.Load( f ) )
					{
						Marker.Draw( b, job.Mark, job.S );
						Marker.Save( b, dst, job.S.Quality );
					}
					res.Done++;
					res.LastDir = dir;
				}
				catch ( Exception ex )
				{
					res.Errors.Add( Path.GetFileName( f ) + ": " + ex.Message );
				}
			}
			if ( job.Mark != null ) job.Mark.Dispose();
			worker.ReportProgress( job.Files.Count, "" );
			e.Result = res;
		}

		void WorkDone( object sender, RunWorkerCompletedEventArgs e )
		{
			bApply.Enabled = true;
			bApply.Text = "APPLY WATERMARK";
			Result res = e.Result as Result;
			if ( res == null ) { status.Text = "Something went wrong: " + ( e.Error != null ? e.Error.Message : "" ); return; }
			lastOut = res.LastDir;
			status.ForeColor = res.Errors.Count == 0 ? GREEN : AMBER;
			status.Text = "✔ " + res.Done + ( res.Done == 1 ? " picture" : " pictures" ) + " done" + ( res.Errors.Count > 0 ? "   ·   " + res.Errors.Count + " failed" : "" );
			bOpen.Visible = res.Done > 0;
			if ( res.Errors.Count > 0 )
				MessageBox.Show( this, string.Join( "\n", res.Errors.Take( 15 ) ) + ( res.Errors.Count > 15 ? "\n…" : "" ), "Some pictures failed", MessageBoxButtons.OK, MessageBoxIcon.Warning );
		}
	}

	/* "page (2)" before "page (10)" */
	class NaturalComparer : IComparer<string>
	{
		[System.Runtime.InteropServices.DllImport( "shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode )]
		static extern int StrCmpLogicalW( string a, string b );
		public int Compare( string a, string b ) { return StrCmpLogicalW( a, b ); }
	}
}
