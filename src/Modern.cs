// The modern look of ComicsUncut Watermark, Collage & Rename: rounded buttons, rounded cards and a slim gradient loading bar.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CuWatermark
{
	static class Look
	{
		/* the colour behind a control: the card's colour when it sits on a card */
		public static Color Behind( Control c )
		{
			Control p = c.Parent;
			if ( p == null ) return c.BackColor;
			RoundPanel rp = p as RoundPanel;
			return rp != null ? rp.Fill : p.BackColor;
		}

		public static Color Mix( Color a, Color b, float t )
		{
			return Color.FromArgb( (int) ( a.R + ( b.R - a.R ) * t ), (int) ( a.G + ( b.G - a.G ) * t ), (int) ( a.B + ( b.B - a.B ) * t ) );
		}

		/* the icons are letters of the Windows icon font (Segoe Fluent Icons on Windows 11, MDL2 Assets on 10) */
		static string iconFont;
		public static string IconFont
		{
			get
			{
				if ( iconFont == null )
				{
					iconFont = "Segoe MDL2 Assets";
					foreach ( FontFamily f in FontFamily.Families ) if ( f.Name == "Segoe Fluent Icons" ) { iconFont = f.Name; break; }
				}
				return iconFont;
			}
		}

		public const string Add = "", Close = "", Refresh = "", Undo = "", Search = "", Folder = "",
			FolderOpen = "", Copy = "", Rename = "", Delete = "", Check = "", Picture = "", List = "",
			Grid = "", Font = "", Explorer = "", Save = "", Play = "", Up = "", Down = "", Info = "";
	}

	/* a button with rounded corners; colours come from BackColor and FlatAppearance (hover, pressed, border) as before;
	   Glyph = an icon (Look.Add, Look.Folder…) drawn before the text */
	class RoundButton : Button
	{
		bool hover, down;
		public int Radius = 8;
		string glyph;
		Font glyphFont;
		public string Glyph { get { return glyph; } set { glyph = value; Invalidate(); } }

		/* a size that fits the icon and the text */
		public void FitWidth( int padding )
		{
			int w = TextRenderer.MeasureText( Text, Font ).Width + 2 * padding;
			if ( !string.IsNullOrEmpty( glyph ) ) w += Font.Height + 8;
			Width = w;
		}

		protected override void Dispose( bool disposing )
		{
			if ( disposing && glyphFont != null ) glyphFont.Dispose();
			base.Dispose( disposing );
		}

		public RoundButton()
		{
			SetStyle( ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true );
		}

		protected override void OnMouseEnter( EventArgs e ) { hover = true; Invalidate(); base.OnMouseEnter( e ); }
		protected override void OnMouseLeave( EventArgs e ) { hover = down = false; Invalidate(); base.OnMouseLeave( e ); }
		protected override void OnMouseDown( MouseEventArgs e ) { if ( e.Button == MouseButtons.Left ) { down = true; Invalidate(); } base.OnMouseDown( e ); }
		protected override void OnMouseUp( MouseEventArgs e ) { down = false; Invalidate(); base.OnMouseUp( e ); }
		protected override void OnEnabledChanged( EventArgs e ) { Invalidate(); base.OnEnabledChanged( e ); }

		protected override void OnPaint( PaintEventArgs e )
		{
			Graphics g = e.Graphics;
			Color behind = Look.Behind( this );
			g.Clear( behind );
			g.SmoothingMode = SmoothingMode.AntiAlias;
			Color fill = BackColor;
			if ( Enabled && down && !FlatAppearance.MouseDownBackColor.IsEmpty ) fill = FlatAppearance.MouseDownBackColor;
			else if ( Enabled && hover && !FlatAppearance.MouseOverBackColor.IsEmpty ) fill = FlatAppearance.MouseOverBackColor;
			if ( !Enabled ) fill = Look.Mix( fill, behind, 0.45f );
			RectangleF r = new RectangleF( 0.5f, 0.5f, Width - 1.5f, Height - 1.5f );
			using ( GraphicsPath p = Collage.Round( r, Radius ) )
			{
				using ( Brush b = new SolidBrush( fill ) ) g.FillPath( b, p );
				Color line = FlatAppearance.BorderColor;
				if ( !line.IsEmpty && line != fill && line != BackColor )
					using ( Pen pen = new Pen( Enabled ? line : Look.Mix( line, behind, 0.5f ) ) ) g.DrawPath( pen, p );
			}
			Color fore = Enabled ? ForeColor : Look.Mix( ForeColor, behind, 0.5f );
			if ( string.IsNullOrEmpty( glyph ) )
			{
				TextRenderer.DrawText( g, Text, Font, ClientRectangle, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix );
				return;
			}
			/* icon + text, centred together */
			if ( glyphFont == null || Math.Abs( glyphFont.Size - Font.Size * 1.15f ) > 0.1f ) glyphFont = new Font( Look.IconFont, Font.Size * 1.15f );
			Size gs = TextRenderer.MeasureText( g, glyph, glyphFont, Size.Empty, TextFormatFlags.NoPadding );
			Size ts = Text == "" ? Size.Empty : TextRenderer.MeasureText( g, Text, Font, Size.Empty, TextFormatFlags.NoPadding );
			int gap = Text == "" ? 0 : 8, x = ( Width - gs.Width - gap - ts.Width ) / 2;
			TextRenderer.DrawText( g, glyph, glyphFont, new Rectangle( x, 0, gs.Width, Height ), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding );
			if ( Text != "" ) TextRenderer.DrawText( g, Text, Font, new Rectangle( x + gs.Width + gap, 0, ts.Width + 4, Height ), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix );
		}
	}

	/* a big button of the side menu: icon, title, one line under it; the chosen one is a pink -> purple pill */
	class NavButton : Control
	{
		bool hover, selected;
		public string Glyph = "", Title = "", Sub = "";
		public Color Muted = Color.FromArgb( 160, 168, 190 );

		public NavButton()
		{
			SetStyle( ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true );
			BackColor = Color.Transparent;
			Cursor = Cursors.Hand;
			Height = 62;
		}

		public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
		protected override void OnMouseEnter( EventArgs e ) { hover = true; Invalidate(); base.OnMouseEnter( e ); }
		protected override void OnMouseLeave( EventArgs e ) { hover = false; Invalidate(); base.OnMouseLeave( e ); }

		protected override void OnPaint( PaintEventArgs e )
		{
			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			RectangleF r = new RectangleF( 0.5f, 0.5f, Width - 1.5f, Height - 1.5f );
			if ( selected || hover )
				using ( GraphicsPath p = Collage.Round( r, 12 ) )
				{
					if ( selected )
						using ( LinearGradientBrush b = new LinearGradientBrush( new RectangleF( 0, 0, Width, Height ), Color.FromArgb( 255, 51, 102 ), Color.FromArgb( 150, 70, 230 ), 0f ) )
							g.FillPath( b, p );
					else
						using ( Brush b = new SolidBrush( Color.FromArgb( 34, 255, 255, 255 ) ) ) g.FillPath( b, p );
				}
			Color title = selected ? Color.White : Color.FromArgb( 232, 236, 244 );
			Color sub = selected ? Color.FromArgb( 235, 255, 255, 255 ) : Muted;
			using ( Font gf = new Font( Look.IconFont, 17f ) )
				TextRenderer.DrawText( g, Glyph, gf, new Rectangle( 14, 0, 34, Height ), title, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding );
			using ( Font tf = new Font( "Segoe UI Semibold", 11f ) )
				TextRenderer.DrawText( g, Title, tf, new Rectangle( 58, 10, Width - 62, 24 ), title, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis );
			using ( Font sf = new Font( "Segoe UI", 8.5f ) )
				TextRenderer.DrawText( g, Sub, sf, new Rectangle( 58, 34, Width - 62, 20 ), sub, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis );
		}
	}

	/* a card: rounded, in its own colour on the colour of what it sits on */
	class RoundPanel : Panel
	{
		public Color Fill = Color.FromArgb( 26, 31, 38 );
		public int Radius = 10;

		public RoundPanel()
		{
			SetStyle( ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true );
		}

		protected override void OnPaintBackground( PaintEventArgs e )
		{
			e.Graphics.Clear( Parent != null ? Parent.BackColor : BackColor );
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using ( GraphicsPath p = Collage.Round( new RectangleF( 0, 0, Width - 1, Height - 1 ), Radius ) )
			using ( Brush b = new SolidBrush( Fill ) )
				e.Graphics.FillPath( b, p );
		}
	}

	/* a slim loading bar: pink -> purple, glides to its value; Busy = "working, no idea how long" (a moving shine) */
	class SlimBar : Control
	{
		int max = 100, val;
		float shown, phase;
		bool busy;
		readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
		public Color Track = Color.FromArgb( 44, 52, 72 ), From = Color.FromArgb( 255, 51, 102 ), To = Color.FromArgb( 168, 85, 247 );

		public SlimBar()
		{
			SetStyle( ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true );
			Height = 6;
			anim.Interval = 16;
			anim.Tick += delegate
			{
				if ( busy ) phase = ( phase + 0.011f ) % 1f;
				else
				{
					float target = val / (float) max;
					shown += ( target - shown ) * 0.2f;
					if ( Math.Abs( target - shown ) < 0.002f ) { shown = target; anim.Stop(); }
				}
				Invalidate();
			};
		}

		public int Maximum { get { return max; } set { max = Math.Max( 1, value ); if ( val > max ) val = max; Glide(); } }
		public int Value { get { return val; } set { val = Math.Max( 0, Math.Min( max, value ) ); Glide(); } }
		public bool Busy { get { return busy; } set { if ( busy == value ) return; busy = value; if ( !busy ) shown = val / (float) max; Glide(); } }

		/* back to empty at once (no gliding down) */
		public void Reset() { val = 0; shown = 0; busy = false; anim.Stop(); Invalidate(); }

		void Glide()
		{
			if ( !anim.Enabled ) anim.Start();
			Invalidate();
		}

		protected override void OnPaint( PaintEventArgs e )
		{
			Graphics g = e.Graphics;
			g.Clear( Look.Behind( this ) );
			g.SmoothingMode = SmoothingMode.AntiAlias;
			RectangleF r = new RectangleF( 0, 0, Width - 1, Height - 1 );
			float rad = Height / 2f;
			using ( GraphicsPath track = Collage.Round( r, rad ) )
			{
				using ( Brush tb = new SolidBrush( Track ) ) g.FillPath( tb, track );
				RectangleF f;
				if ( busy )
				{
					float w = Width * 0.28f;
					f = new RectangleF( -w + ( Width + w ) * phase, 0, w, Height - 1 );
				}
				else
				{
					float w = ( Width - 1 ) * shown;
					if ( w < 1 ) return;
					f = new RectangleF( 0, 0, Math.Max( w, Height ), Height - 1 );
				}
				g.SetClip( track );
				using ( LinearGradientBrush gb = new LinearGradientBrush( new RectangleF( 0, 0, Math.Max( 2, Width ), Height ), From, To, 0f ) )
				using ( GraphicsPath fp = Collage.Round( f, rad ) )
					g.FillPath( gb, fp );
				g.ResetClip();
			}
		}

		protected override void Dispose( bool disposing )
		{
			if ( disposing ) anim.Dispose();
			base.Dispose( disposing );
		}
	}
}
