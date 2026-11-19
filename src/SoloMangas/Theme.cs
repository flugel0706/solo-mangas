using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SoloMangasNative
{
    /// <summary>
    /// Temas visuais do Solo Mangás. O tema "sangue" é o visual clássico
    /// (preto profundo + vermelho vivo). O tema "cod" é inspirado em jogos de
    /// guerra modernos: carvão/oliva, dourado e um papel de parede real.
    /// Todos os pincéis são recriados em <see cref="Init"/> antes da janela abrir.
    /// </summary>
    public static class Theme
    {
        public const string Sangue = "sangue";
        public const string Cod = "cod";

        private sealed class Palette
        {
            public string Id;
            public string Label;
            public Color Bg, Panel, Panel2, Panel3, Border, Text, Muted;
            public Color Accent, Accent2, AccentDeep, Ember, Good, Gold;
            public Color HoverPrimary, HoverGhost, HoverSoft, HoverDanger;
            public Color HoverIcon, HoverChip, HoverLink, HoverPill;
            public Color SidebarTop, SidebarBottom;
            public Color Glow1, Glow2;
            public Color ToastBg, ToastErrorBg;
            public Color Header;
            public Color GradPanelTop, GradPanelBottom;
            public Color SkeletonTop, SkeletonBottom;
            public string DisplayFont;
        }

        // ---------------- Estado do tema ----------------
        public static string Id { get; private set; } = Sangue;
        public static string Name { get; private set; } = "Sangue & aço";
        public static bool HasWallpaper => Id == Cod;
        public static ImageSource Wallpaper { get; private set; }
        public static string WallpaperCredit =>
            "Foto de fundo: UK Ministry of Defence — Soldier Silhouetted in Afghanistan (OGL v1.0).";

        // ---------------- Paleta ----------------
        public static Color BgColor { get; private set; }
        public static Color PanelColor { get; private set; }
        public static Color Panel2Color { get; private set; }
        public static Color Panel3Color { get; private set; }
        public static Color BorderColor { get; private set; }
        public static Color TextColor { get; private set; }
        public static Color MutedColor { get; private set; }
        public static Color AccentColor { get; private set; }
        public static Color Accent2Color { get; private set; }
        public static Color AccentDeepColor { get; private set; }
        public static Color EmberColor { get; private set; }
        public static Color GoodColor { get; private set; }
        public static Color GoldColor { get; private set; }
        public static Color Glow1Color { get; private set; }
        public static Color Glow2Color { get; private set; }

        public static SolidColorBrush Bg { get; private set; }
        public static SolidColorBrush Panel { get; private set; }
        public static SolidColorBrush Panel2 { get; private set; }
        public static SolidColorBrush Panel3 { get; private set; }
        public static SolidColorBrush Line { get; private set; }
        public static SolidColorBrush Text { get; private set; }
        public static SolidColorBrush Muted { get; private set; }
        public static SolidColorBrush Accent { get; private set; }
        public static SolidColorBrush Accent2 { get; private set; }
        public static SolidColorBrush Ember { get; private set; }
        public static SolidColorBrush Good { get; private set; }
        public static SolidColorBrush Gold { get; private set; }
        public static SolidColorBrush AccentSoft { get; private set; }
        public static SolidColorBrush AccentLine { get; private set; }
        public static SolidColorBrush DangerSoft { get; private set; }
        public static SolidColorBrush Glass { get; private set; }
        public static SolidColorBrush Toast { get; private set; }
        public static SolidColorBrush ToastError { get; private set; }
        public static SolidColorBrush Header { get; private set; }

        public static LinearGradientBrush Grad { get; private set; }
        public static LinearGradientBrush GradPanel { get; private set; }
        public static LinearGradientBrush GradEdge { get; private set; }
        public static LinearGradientBrush SkeletonGrad { get; private set; }
        public static LinearGradientBrush Sidebar { get; private set; }

        public static FontFamily Body { get; private set; } = new FontFamily("Segoe UI");
        public static FontFamily Display { get; private set; } = new FontFamily("Segoe UI Variable Display, Segoe UI Semibold, Segoe UI");

        // Cores de hover usadas na construção dos estilos.
        private static Color _hoverPrimary, _hoverGhost, _hoverSoft, _hoverDanger;
        private static Color _hoverIcon, _hoverChip, _hoverLink, _hoverPill;

        static Theme()
        {
            ApplyPalette(Sangue);
        }

        // ------------------------------------------------------------------
        // Paletas
        // ------------------------------------------------------------------
        private static void ApplyPalette(string id)
        {
            var p = string.Equals(id, Cod, StringComparison.OrdinalIgnoreCase) ? CodPalette() : SanguePalette();
            Id = p.Id;
            Name = p.Label;
            BgColor = p.Bg;
            PanelColor = p.Panel;
            Panel2Color = p.Panel2;
            Panel3Color = p.Panel3;
            BorderColor = p.Border;
            TextColor = p.Text;
            MutedColor = p.Muted;
            AccentColor = p.Accent;
            Accent2Color = p.Accent2;
            AccentDeepColor = p.AccentDeep;
            EmberColor = p.Ember;
            GoodColor = p.Good;
            GoldColor = p.Gold;
            Glow1Color = p.Glow1;
            Glow2Color = p.Glow2;

            Bg = Frozen(p.Bg);
            Panel = Frozen(p.Panel);
            Panel2 = Frozen(p.Panel2);
            Panel3 = Frozen(p.Panel3);
            Line = Frozen(p.Border);
            Text = Frozen(p.Text);
            Muted = Frozen(p.Muted);
            Accent = Frozen(p.Accent);
            Accent2 = Frozen(p.Accent2);
            Ember = Frozen(p.Ember);
            Good = Frozen(p.Good);
            Gold = Frozen(p.Gold);
            AccentSoft = Frozen(Alpha(0x33, p.Accent));
            AccentLine = Frozen(Alpha(0x8A, p.Accent));
            DangerSoft = Frozen(Alpha(0x30, Color.FromRgb(0xFF, 0x3D, 0x5A)));
            Glass = Frozen(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
            Toast = Frozen(p.ToastBg);
            ToastError = Frozen(p.ToastErrorBg);
            Header = Frozen(p.Header);

            Grad = Gradient(p.Accent2, p.AccentDeep, true);
            GradPanel = Gradient(p.GradPanelTop, p.GradPanelBottom, false);
            GradEdge = Gradient(Alpha(0xBB, p.Accent), Alpha(0x00, p.Accent), true);
            SkeletonGrad = Gradient(p.SkeletonTop, p.SkeletonBottom, true);
            Sidebar = Gradient(p.SidebarTop, p.SidebarBottom, false, new Point(0, 0), new Point(0, 1));

            Body = new FontFamily("Segoe UI");
            Display = new FontFamily(p.DisplayFont);
            _hoverPrimary = p.HoverPrimary;
            _hoverGhost = p.HoverGhost;
            _hoverSoft = p.HoverSoft;
            _hoverDanger = p.HoverDanger;
            _hoverIcon = p.HoverIcon;
            _hoverChip = p.HoverChip;
            _hoverLink = p.HoverLink;
            _hoverPill = p.HoverPill;
        }

        private static Palette SanguePalette() => new Palette
        {
            Id = Sangue,
            Label = "Sangue & aço",
            Bg = Color.FromRgb(0x06, 0x03, 0x05),
            Panel = Color.FromRgb(0x10, 0x0A, 0x0D),
            Panel2 = Color.FromRgb(0x19, 0x0E, 0x13),
            Panel3 = Color.FromRgb(0x23, 0x13, 0x19),
            Border = Color.FromRgb(0x35, 0x17, 0x20),
            Text = Color.FromRgb(0xF8, 0xF1, 0xF3),
            Muted = Color.FromRgb(0xA6, 0x8C, 0x93),
            Accent = Color.FromRgb(0xE8, 0x11, 0x38),
            Accent2 = Color.FromRgb(0xFF, 0x4A, 0x66),
            AccentDeep = Color.FromRgb(0x7C, 0x09, 0x1B),
            Ember = Color.FromRgb(0xFF, 0x8A, 0x4D),
            Good = Color.FromRgb(0x4F, 0xE0, 0xA6),
            Gold = Color.FromRgb(0xF6, 0xC6, 0x50),
            HoverPrimary = Color.FromRgb(0xE0, 0x14, 0x34),
            HoverGhost = Color.FromRgb(0x2E, 0x16, 0x1D),
            HoverSoft = Color.FromRgb(0x66, 0x1B, 0x29),
            HoverDanger = Color.FromRgb(0x58, 0x16, 0x21),
            HoverIcon = Color.FromRgb(0x3A, 0x1A, 0x23),
            HoverChip = Color.FromRgb(0x44, 0x1C, 0x27),
            HoverLink = Color.FromRgb(0x2A, 0x10, 0x17),
            HoverPill = Color.FromRgb(0x3A, 0x1A, 0x23),
            SidebarTop = Color.FromRgb(0x14, 0x08, 0x0C),
            SidebarBottom = Color.FromRgb(0x0B, 0x05, 0x07),
            Glow1 = Color.FromArgb(0x38, 0xE0, 0x14, 0x34),
            Glow2 = Color.FromArgb(0x1E, 0xFF, 0x4A, 0x66),
            ToastBg = Color.FromArgb(0xF4, 0x22, 0x0D, 0x13),
            ToastErrorBg = Color.FromArgb(0xF4, 0x2C, 0x0C, 0x14),
            Header = Color.FromArgb(0xF7, 0x06, 0x03, 0x05),
            GradPanelTop = Color.FromRgb(0x1E, 0x0D, 0x13),
            GradPanelBottom = Color.FromRgb(0x0B, 0x05, 0x08),
            SkeletonTop = Color.FromRgb(0x1C, 0x0E, 0x14),
            SkeletonBottom = Color.FromRgb(0x2B, 0x16, 0x1D),
            DisplayFont = "Segoe UI Variable Display, Segoe UI Semibold, Segoe UI",
        };

        private static Palette CodPalette() => new Palette
        {
            Id = Cod,
            Label = "Call of Duty",
            Bg = Color.FromRgb(0x07, 0x0A, 0x07),
            // Painéis translúcidos: o papel de parede aparece por trás sem
            // comprometer a leitura (o MainWindow aplica uma película escura).
            Panel = Color.FromArgb(0xDC, 0x12, 0x1B, 0x13),
            Panel2 = Color.FromArgb(0xE8, 0x1A, 0x24, 0x19),
            Panel3 = Color.FromArgb(0xF2, 0x23, 0x2F, 0x20),
            Border = Color.FromRgb(0x46, 0x5C, 0x39),
            Text = Color.FromRgb(0xF3, 0xF6, 0xEC),
            Muted = Color.FromRgb(0xA9, 0xB7, 0x9C),
            Accent = Color.FromRgb(0xD6, 0xA6, 0x2F),
            Accent2 = Color.FromRgb(0xF2, 0xC7, 0x5B),
            AccentDeep = Color.FromRgb(0x6A, 0x4D, 0x10),
            Ember = Color.FromRgb(0xE0, 0x8A, 0x3C),
            Good = Color.FromRgb(0x7C, 0xD9, 0xA8),
            Gold = Color.FromRgb(0xEA, 0xC8, 0x69),
            HoverPrimary = Color.FromRgb(0xA9, 0x7F, 0x1E),
            HoverGhost = Color.FromRgb(0x1E, 0x2A, 0x18),
            HoverSoft = Color.FromRgb(0x4B, 0x3A, 0x13),
            HoverDanger = Color.FromRgb(0x4E, 0x24, 0x18),
            HoverIcon = Color.FromRgb(0x2A, 0x3A, 0x22),
            HoverChip = Color.FromRgb(0x34, 0x46, 0x2A),
            HoverLink = Color.FromRgb(0x20, 0x2C, 0x19),
            HoverPill = Color.FromRgb(0x2A, 0x3A, 0x22),
            SidebarTop = Color.FromArgb(0xDD, 0x0E, 0x15, 0x0F),
            SidebarBottom = Color.FromArgb(0xF2, 0x08, 0x0C, 0x08),
            Glow1 = Color.FromArgb(0x3C, 0xD6, 0xA6, 0x2F),
            Glow2 = Color.FromArgb(0x24, 0x5E, 0x7A, 0x4B),
            ToastBg = Color.FromArgb(0xF4, 0x13, 0x1B, 0x12),
            ToastErrorBg = Color.FromArgb(0xF4, 0x2A, 0x15, 0x10),
            Header = Color.FromArgb(0xE6, 0x0A, 0x0F, 0x0A),
            GradPanelTop = Color.FromRgb(0x23, 0x2F, 0x1E),
            GradPanelBottom = Color.FromRgb(0x0A, 0x0F, 0x09),
            SkeletonTop = Color.FromRgb(0x1A, 0x24, 0x17),
            SkeletonBottom = Color.FromRgb(0x2A, 0x39, 0x21),
            DisplayFont = "Bahnschrift SemiBold Condensed, Bahnschrift, Segoe UI Semibold, Segoe UI",
        };

        // ------------------------------------------------------------------
        // Inicialização
        // ------------------------------------------------------------------
        public static void Init(Application app)
        {
            var saved = LocalStore.Instance.Settings.Theme;
            ApplyPalette(string.Equals(saved, Cod, StringComparison.OrdinalIgnoreCase) ? Cod : Sangue);
            Wallpaper = HasWallpaper ? TryLoadWallpaper() : null;

            app.Resources["Bg"] = Bg;
            app.Resources["Panel"] = Panel;
            app.Resources["Panel2"] = Panel2;
            app.Resources["Panel3"] = Panel3;
            app.Resources["Line"] = Line;
            app.Resources["Fg"] = Text;
            app.Resources["Muted"] = Muted;
            app.Resources["Accent"] = Accent;
            app.Resources["Accent2"] = Accent2;
            app.Resources["AccentSoft"] = AccentSoft;
            app.Resources["Good"] = Good;
            app.Resources["Gold"] = Gold;
            app.Resources["Grad"] = Grad;

            var textBase = new Style(typeof(TextBlock));
            textBase.Setters.Add(new Setter(TextBlock.ForegroundProperty, Text));
            textBase.Setters.Add(new Setter(TextBlock.FontFamilyProperty, Body));
            textBase.Setters.Add(new Setter(TextBlock.FontSizeProperty, 13.5));
            textBase.Setters.Add(new Setter(TextOptions.TextFormattingModeProperty, TextFormattingMode.Ideal));
            app.Resources[typeof(TextBlock)] = textBase;

            app.Resources["PrimaryButton"] = ButtonStyle(Grad,
                Id == Cod ? new SolidColorBrush(Color.FromRgb(0x1B, 0x15, 0x06)) : Brushes.White,
                _hoverPrimary, null, 11);
            app.Resources["GhostButton"] = ButtonStyle(Brushes.Transparent, Text, _hoverGhost, Line, 11);
            app.Resources["SoftButton"] = ButtonStyle(AccentSoft, Accent2, _hoverSoft, AccentLine, 11);
            app.Resources["DangerButton"] = ButtonStyle(DangerSoft, Accent2, _hoverDanger, AccentLine, 11);
            app.Resources["IconButton"] = ButtonStyle(Panel2, Text, _hoverIcon, Line, 10);
            app.Resources["PillButton"] = ButtonStyle(Panel2, Text, _hoverPill, Line, 17);
            app.Resources["LinkButton"] = LinkStyle();
            app.Resources["NavButton"] = NavStyle();
            app.Resources["ChipButton"] = ChipStyle();

            app.Resources[typeof(TextBox)] = InputStyle();
            app.Resources[typeof(ScrollBar)] = ScrollBarStyle();
            app.Resources[typeof(ToolTip)] = ToolTipStyle();

            var ctx = new Style(typeof(ContextMenu));
            ctx.Setters.Add(new Setter(ContextMenu.BackgroundProperty, Panel));
            ctx.Setters.Add(new Setter(ContextMenu.ForegroundProperty, Text));
            ctx.Setters.Add(new Setter(ContextMenu.BorderBrushProperty, Line));
            ctx.Setters.Add(new Setter(ContextMenu.BorderThicknessProperty, new Thickness(1)));
            ctx.Setters.Add(new Setter(ContextMenu.PaddingProperty, new Thickness(5)));
            ctx.Setters.Add(new Setter(ContextMenu.HasDropShadowProperty, true));
            app.Resources[typeof(ContextMenu)] = ctx;

            var menuItem = new Style(typeof(MenuItem));
            menuItem.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
            menuItem.Setters.Add(new Setter(MenuItem.ForegroundProperty, Text));
            menuItem.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(12, 7, 12, 7)));
            menuItem.Setters.Add(new Setter(MenuItem.FontSizeProperty, 13.5));
            menuItem.Setters.Add(new Setter(MenuItem.CursorProperty, System.Windows.Input.Cursors.Hand));
            menuItem.Setters.Add(new Setter(MenuItem.TemplateProperty, MenuItemTemplate()));

            var menuDisabled = new Trigger { Property = MenuItem.IsEnabledProperty, Value = false };
            menuDisabled.Setters.Add(new Setter(MenuItem.OpacityProperty, 0.45));
            menuItem.Triggers.Add(menuDisabled);
            app.Resources[typeof(MenuItem)] = menuItem;
        }

        private static ImageSource TryLoadWallpaper()
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri("pack://application:,,,/Assets/cod-theme.jpg", UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static LinearGradientBrush Gradient(Color a, Color b, bool diagonal) =>
            Gradient(a, b, diagonal, new Point(0, 0), diagonal ? new Point(1, 1) : new Point(1, 0));

        private static LinearGradientBrush Gradient(Color a, Color b, bool diagonal, Point start, Point end)
        {
            var g = new LinearGradientBrush(a, b, start, end);
            g.Freeze();
            return g;
        }

        private static Color Alpha(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>Cor de fundo do tema com transparência (véus e gradientes).</summary>
        public static Color Shade(byte alpha) => Color.FromArgb(alpha, BgColor.R, BgColor.G, BgColor.B);

        /// <summary>Cor de destaque com transparência (brilhos, películas e hovers).</summary>
        public static Color Tint(byte alpha) => Color.FromArgb(alpha, AccentColor.R, AccentColor.G, AccentColor.B);

        // ------------------------------------------------------------------
        // Modelos de controle
        // ------------------------------------------------------------------
        private static ControlTemplate MenuItemTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            border.AppendChild(cp);

            var tpl = new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
            var over = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, Panel2) { TargetName = "Bd" });
            tpl.Triggers.Add(over);
            return tpl;
        }

        private static Style ButtonStyle(Brush bg, Brush fg, Color hover, Brush border, double radius)
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, bg));
            style.Setters.Add(new Setter(Button.ForegroundProperty, fg));
            style.Setters.Add(new Setter(Button.BorderBrushProperty, border ?? Brushes.Transparent));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(border == null ? 0 : 1)));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(15, 8, 15, 8)));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 13.5));
            style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Button.FontFamilyProperty, Body));
            style.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Button.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(Button.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Button.TemplateProperty, ButtonTemplate(radius, hover)));

            var disabled = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Button.OpacityProperty, 0.4));
            style.Triggers.Add(disabled);
            return style;
        }

        /// <summary>
        /// Base com brilho de hover em camada própria: preserva gradientes
        /// (o fundo original não é substituído, só recebe uma película).
        /// </summary>
        private static ControlTemplate ButtonTemplate(double radius, Color hover)
        {
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });

            var inner = new FrameworkElementFactory(typeof(Grid));
            var overlay = new FrameworkElementFactory(typeof(Border));
            overlay.Name = "Hover";
            overlay.SetValue(Border.CornerRadiusProperty, new CornerRadius(Math.Max(0, radius - 1)));
            overlay.SetValue(Border.BackgroundProperty, Frozen(hover));
            overlay.SetValue(UIElement.OpacityProperty, 0.0);
            overlay.SetValue(UIElement.IsHitTestVisibleProperty, false);
            inner.AppendChild(overlay);

            var pad = new FrameworkElementFactory(typeof(Border));
            pad.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            pad.AppendChild(cp);
            inner.AppendChild(pad);
            bd.AppendChild(inner);

            var tpl = new ControlTemplate(typeof(Button)) { VisualTree = bd };
            var over = new Trigger { Property = ButtonBase.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(UIElement.OpacityProperty, 0.62, "Hover"));
            tpl.Triggers.Add(over);
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82, "Bd"));
            tpl.Triggers.Add(pressed);
            return tpl;
        }

        private static Style LinkStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Button.ForegroundProperty, Accent2));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(2, 2, 2, 2)));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 12.5));
            style.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Button.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Button.TemplateProperty, ButtonTemplate(6, _hoverLink)));
            return style;
        }

        /// <summary>
        /// Item de navegação: barra vertical de acento aparece quando BorderBrush
        /// é definido como a cor de destaque (estado ativo controlado pelo MainWindow).
        /// </summary>
        private static Style NavStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Button.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Button.ForegroundProperty, Muted));
            style.Setters.Add(new Setter(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(13, 9, 12, 9)));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 14.0));
            style.Setters.Add(new Setter(Button.FontFamilyProperty, Body));
            style.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Button.FocusVisualStyleProperty, null));

            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            bd.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });

            var grid = new FrameworkElementFactory(typeof(Grid));

            var bar = new FrameworkElementFactory(typeof(Border));
            bar.Name = "Bar";
            bar.SetValue(Border.WidthProperty, 3.0);
            bar.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            bar.SetValue(Border.MarginProperty, new Thickness(0, 9, 0, 9));
            bar.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            bar.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            bar.SetBinding(Border.BackgroundProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            grid.AppendChild(bar);

            var pad = new FrameworkElementFactory(typeof(Border));
            pad.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            pad.AppendChild(cp);
            grid.AppendChild(pad);

            bd.AppendChild(grid);
            var tpl = new ControlTemplate(typeof(Button)) { VisualTree = bd };
            var over = new Trigger { Property = ButtonBase.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, Panel2) { TargetName = "Bd" });
            tpl.Triggers.Add(over);
            style.Setters.Add(new Setter(Button.TemplateProperty, tpl));
            return style;
        }

        private static Style ChipStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, Panel2));
            style.Setters.Add(new Setter(Button.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Button.BorderBrushProperty, Line));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(13, 5, 13, 5)));
            style.Setters.Add(new Setter(Button.MarginProperty, new Thickness(0, 0, 7, 7)));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 12.5));
            style.Setters.Add(new Setter(Button.FontFamilyProperty, Body));
            style.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Button.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Button.TemplateProperty, ButtonTemplate(16, _hoverChip)));
            return style;
        }

        private static Style InputStyle()
        {
            var style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(TextBox.BackgroundProperty, Panel2));
            style.Setters.Add(new Setter(TextBox.ForegroundProperty, Text));
            style.Setters.Add(new Setter(TextBox.BorderBrushProperty, Line));
            style.Setters.Add(new Setter(TextBox.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(12, 8, 12, 8)));
            style.Setters.Add(new Setter(TextBox.CaretBrushProperty, Accent2));
            style.Setters.Add(new Setter(TextBox.FontSizeProperty, 14.0));
            style.Setters.Add(new Setter(TextBox.FontFamilyProperty, Body));
            style.Setters.Add(new Setter(TextBox.SelectionBrushProperty, AccentSoft));
            style.Setters.Add(new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBox.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(TextBox.TemplateProperty, InputTemplate()));

            var focused = new Trigger { Property = TextBox.IsKeyboardFocusWithinProperty, Value = true };
            focused.Setters.Add(new Setter(TextBox.BorderBrushProperty, AccentLine));
            focused.Setters.Add(new Setter(TextBox.BackgroundProperty, Panel3));
            style.Triggers.Add(focused);
            return style;
        }

        private static ControlTemplate InputTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            var host = new FrameworkElementFactory(typeof(ScrollViewer));
            host.Name = "PART_ContentHost";
            host.SetBinding(ScrollViewer.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            border.AppendChild(host);
            return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
        }

        private static Style ScrollBarStyle()
        {
            var style = new Style(typeof(ScrollBar));
            style.Setters.Add(new Setter(ScrollBar.WidthProperty, 10.0));
            style.Setters.Add(new Setter(ScrollBar.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(ScrollBar.TemplateProperty, ScrollBarTemplate()));

            var horizontal = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Horizontal };
            horizontal.Setters.Add(new Setter(ScrollBar.WidthProperty, double.NaN));
            horizontal.Setters.Add(new Setter(ScrollBar.HeightProperty, 12.0));
            style.Triggers.Add(horizontal);
            return style;
        }

        private static ControlTemplate ScrollBarTemplate()
        {
            // Template em XAML: Track exige Thumb/RepeatButtons como propriedades.
            // As cores acompanham o tema atual (vermelho no sangue, dourado no CoD).
            var xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='ScrollBar'>
  <Grid Background='Transparent'>
    <Track Name='PART_Track'
           IsDirectionReversed='True'
           Orientation='{Binding Orientation, RelativeSource={RelativeSource TemplatedParent}}'>
      <Track.Thumb>
        <Thumb MinWidth='22' MinHeight='22'>
          <Thumb.Template>
            <ControlTemplate TargetType='Thumb'>
              <Border x:Name='Tb' CornerRadius='5' Margin='3' Background='__TB__'/>
              <ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'>
                  <Setter TargetName='Tb' Property='Background' Value='__HOVER__'/>
                </Trigger>
                <Trigger Property='IsDragging' Value='True'>
                  <Setter TargetName='Tb' Property='Background' Value='__DRAG__'/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
      <Track.DecreaseRepeatButton>
        <RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/>
      </Track.DecreaseRepeatButton>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/>
      </Track.IncreaseRepeatButton>
    </Track>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='Orientation' Value='Horizontal'>
      <Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
            var track = Id == Cod ? "#4A4430" : "#4A1B26";
            xaml = xaml.Replace("__TB__", track)
                       .Replace("__HOVER__", Hex(AccentColor))
                       .Replace("__DRAG__", Hex(Accent2Color));
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private static string Hex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        private static Style ToolTipStyle()
        {
            var style = new Style(typeof(ToolTip));
            style.Setters.Add(new Setter(ToolTip.BackgroundProperty, Panel3));
            style.Setters.Add(new Setter(ToolTip.ForegroundProperty, Text));
            style.Setters.Add(new Setter(ToolTip.BorderBrushProperty, Line));
            style.Setters.Add(new Setter(ToolTip.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(ToolTip.PaddingProperty, new Thickness(10, 6, 10, 6)));
            style.Setters.Add(new Setter(ToolTip.FontSizeProperty, 12.5));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(cp);
            style.Setters.Add(new Setter(ToolTip.TemplateProperty, new ControlTemplate(typeof(ToolTip)) { VisualTree = border }));
            return style;
        }

        public static TextBlock H1(string text) => new TextBlock
        {
            Text = text,
            FontSize = 31,
            FontWeight = FontWeights.Bold,
            FontFamily = Display,
            Foreground = Text,
            TextWrapping = TextWrapping.Wrap,
        };

        public static TextBlock H2(string text) => new TextBlock
        {
            Text = text,
            FontSize = 18.5,
            FontWeight = FontWeights.Bold,
            FontFamily = Display,
            Foreground = Text,
            Margin = new Thickness(0, 0, 0, 10),
        };

        public static TextBlock Label(string text, double size = 13.5, Brush fg = null, bool bold = false) => new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = fg ?? Text,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
        };

        public static Button Btn(string text, string styleKey = "GhostButton")
        {
            var b = new Button
            {
                Content = text,
                Style = (Style)Application.Current.Resources[styleKey],
            };
            return b;
        }
    }
}
