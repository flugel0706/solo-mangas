using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace SoloMangasNative
{
    /// <summary>
    /// Tema "sangue" do Solo Mangás 2.2: preto mais profundo, vermelho vivo,
    /// detalhes em branco osso e cantos macios. Tudo desenhado em WPF.
    /// </summary>
    public static class Theme
    {
        // ---------------- Paleta ----------------
        public static readonly Color BgColor = Color.FromRgb(0x06, 0x03, 0x05);
        public static readonly Color PanelColor = Color.FromRgb(0x10, 0x0A, 0x0D);
        public static readonly Color Panel2Color = Color.FromRgb(0x19, 0x0E, 0x13);
        public static readonly Color Panel3Color = Color.FromRgb(0x23, 0x13, 0x19);
        public static readonly Color BorderColor = Color.FromRgb(0x35, 0x17, 0x20);
        public static readonly Color TextColor = Color.FromRgb(0xF8, 0xF1, 0xF3);
        public static readonly Color MutedColor = Color.FromRgb(0xA6, 0x8C, 0x93);
        public static readonly Color AccentColor = Color.FromRgb(0xE8, 0x11, 0x38);
        public static readonly Color Accent2Color = Color.FromRgb(0xFF, 0x4A, 0x66);
        public static readonly Color AccentDeepColor = Color.FromRgb(0x7C, 0x09, 0x1B);
        public static readonly Color EmberColor = Color.FromRgb(0xFF, 0x8A, 0x4D);
        public static readonly Color GoodColor = Color.FromRgb(0x4F, 0xE0, 0xA6);
        public static readonly Color GoldColor = Color.FromRgb(0xF6, 0xC6, 0x50);

        public static readonly SolidColorBrush Bg = Frozen(BgColor);
        public static readonly SolidColorBrush Panel = Frozen(PanelColor);
        public static readonly SolidColorBrush Panel2 = Frozen(Panel2Color);
        public static readonly SolidColorBrush Panel3 = Frozen(Panel3Color);
        public static readonly SolidColorBrush Line = Frozen(BorderColor);
        public static readonly SolidColorBrush Text = Frozen(TextColor);
        public static readonly SolidColorBrush Muted = Frozen(MutedColor);
        public static readonly SolidColorBrush Accent = Frozen(AccentColor);
        public static readonly SolidColorBrush Accent2 = Frozen(Accent2Color);
        public static readonly SolidColorBrush Ember = Frozen(EmberColor);
        public static readonly SolidColorBrush Good = Frozen(GoodColor);
        public static readonly SolidColorBrush Gold = Frozen(GoldColor);
        public static readonly SolidColorBrush AccentSoft = Frozen(Color.FromArgb(0x33, AccentColor.R, AccentColor.G, AccentColor.B));
        public static readonly SolidColorBrush AccentLine = Frozen(Color.FromArgb(0x8A, AccentColor.R, AccentColor.G, AccentColor.B));
        public static readonly SolidColorBrush DangerSoft = Frozen(Color.FromArgb(0x30, 0xFF, 0x3D, 0x5A));
        public static readonly SolidColorBrush Glass = Frozen(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));

        public static readonly LinearGradientBrush Grad = Gradient(Accent2Color, AccentDeepColor, true);
        public static readonly LinearGradientBrush GradPanel = Gradient(Color.FromRgb(0x1E, 0x0D, 0x13), Color.FromRgb(0x0B, 0x05, 0x08), false);
        public static readonly LinearGradientBrush GradEdge = Gradient(
            Color.FromArgb(0xBB, AccentColor.R, AccentColor.G, AccentColor.B),
            Color.FromArgb(0x00, AccentColor.R, AccentColor.G, AccentColor.B), true);
        public static readonly LinearGradientBrush SkeletonGrad = Gradient(
            Color.FromRgb(0x1C, 0x0E, 0x14), Color.FromRgb(0x2B, 0x16, 0x1D), true);

        public static readonly FontFamily Body = new FontFamily("Segoe UI");
        public static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI Semibold, Segoe UI");

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static LinearGradientBrush Gradient(Color a, Color b, bool diagonal)
        {
            var g = new LinearGradientBrush(a, b,
                new Point(0, 0),
                diagonal ? new Point(1, 1) : new Point(1, 0));
            g.Freeze();
            return g;
        }

        public static void Init(Application app)
        {
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

            app.Resources["PrimaryButton"] = ButtonStyle(Grad, Brushes.White, Color.FromRgb(0xE0, 0x14, 0x34), null, 11);
            app.Resources["GhostButton"] = ButtonStyle(Brushes.Transparent, Text, Color.FromRgb(0x2E, 0x16, 0x1D), Line, 11);
            app.Resources["SoftButton"] = ButtonStyle(AccentSoft, Accent2, Color.FromRgb(0x66, 0x1B, 0x29), AccentLine, 11);
            app.Resources["DangerButton"] = ButtonStyle(DangerSoft, Accent2, Color.FromRgb(0x58, 0x16, 0x21), AccentLine, 11);
            app.Resources["IconButton"] = ButtonStyle(Panel2, Text, Color.FromRgb(0x3A, 0x1A, 0x23), Line, 10);
            app.Resources["PillButton"] = ButtonStyle(Panel2, Text, Color.FromRgb(0x3A, 0x1A, 0x23), Line, 17);
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
            overlay.SetValue(Border.BackgroundProperty, new SolidColorBrush(hover));
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
            style.Setters.Add(new Setter(Button.TemplateProperty, ButtonTemplate(6, Color.FromRgb(0x2A, 0x10, 0x17))));
            return style;
        }

        /// <summary>
        /// Item de navegação: barra vertical de acento aparece quando BorderBrush
        /// é definido como vermelho (estado ativo controlado pelo MainWindow).
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
            style.Setters.Add(new Setter(Button.TemplateProperty, ButtonTemplate(16, Color.FromRgb(0x44, 0x1C, 0x27))));
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
            const string xaml = @"
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
              <Border x:Name='Tb' CornerRadius='5' Margin='3' Background='#4A1B26'/>
              <ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'>
                  <Setter TargetName='Tb' Property='Background' Value='#E81138'/>
                </Trigger>
                <Trigger Property='IsDragging' Value='True'>
                  <Setter TargetName='Tb' Property='Background' Value='#FF4A66'/>
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
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

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
