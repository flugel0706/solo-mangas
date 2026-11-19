using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SoloMangasNative
{
    /// <summary>
    /// Card de obra: capa arredondada, fade-in da imagem e reação de hover.
    /// A capa é carregada sob demanda pelo MangaGrid (só quando o card chega
    /// perto da área visível), o que evita dezenas de downloads simultâneos
    /// ao abrir uma lista grande.
    /// </summary>
    public sealed class MangaCard : Border
    {
        private const int CoverDecode = 380;
        private const double CardWidth = 172;
        private const double CoverHeight = 224;

        private readonly Image _img;
        private readonly Border _coverBox;
        private readonly Border _skeleton;
        private readonly TextBlock _letter;
        private readonly TextBlock _title;
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _lift = new TranslateTransform(0, 0);
        private readonly Border _heart;
        private readonly TextBlock _heartGlyph;
        private readonly Border _sheen;

        private bool _coverLoaded;
        private bool _coverLoading;
        private bool _coverWanted;

        public Manga Manga { get; }
        public event Action<Manga> Clicked;

        public MangaCard(Manga m, bool showSource = false)
            : this(m, showSource, true)
        {
        }

        public MangaCard(Manga m, bool showSource, bool autoLoad)
        {
            Manga = m;
            Width = CardWidth;
            Margin = new Thickness(0, 0, 16, 22);
            Background = Theme.Panel;
            BorderBrush = Theme.Line;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(16);
            Padding = new Thickness(9);
            Cursor = Cursors.Hand;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = new TransformGroup { Children = { _scale, _lift } };

            var root = new StackPanel();

            _coverBox = new Border
            {
                Height = CoverHeight,
                CornerRadius = new CornerRadius(11),
                Background = Theme.Panel2,
                ClipToBounds = true,
            };
            Ui.RoundClip(_coverBox, 11);
            var grid = new Grid();

            // Fundo de carregamento: gradiente quente que some quando a capa chega.
            _skeleton = new Border { Background = Theme.SkeletonGrad };
            grid.Children.Add(_skeleton);

            _letter = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(m.Title) ? "?" : m.Title.Trim().Substring(0, 1).ToUpperInvariant(),
                FontSize = 52,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = Theme.AccentSoft,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(_letter);

            _img = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            grid.Children.Add(_img);

            // Película de acento no hover: dá vida à capa sem trocar a imagem.
            _sheen = new Border
            {
                IsHitTestVisible = false,
                Opacity = 0,
                Background = new LinearGradientBrush(
                    Theme.Tint(0x00),
                    Theme.Tint(0x42),
                    new Point(0, 0), new Point(1, 1)),
            };
            grid.Children.Add(_sheen);

            // linha de brilho no topo da capa (profundidade)
            grid.Children.Add(new Border
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false,
            });

            if (m.Adult)
            {
                var badge = new Border
                {
                    Background = Theme.Accent2,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(6, 2, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(7),
                    Child = new TextBlock
                    {
                        Text = "+18",
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                    },
                };
                grid.Children.Add(badge);
            }

            // Favoritar direto do card, sem abrir a obra.
            var fav = LocalStore.Instance.IsFavorite(m.Source, m.Id);
            _heartGlyph = new TextBlock
            {
                Text = fav ? "♥" : "♡",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = fav ? Theme.Accent2 : Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _heart = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Theme.Shade(0xB8)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(7),
                Cursor = Cursors.Hand,
                ToolTip = fav ? "Remover dos favoritos" : "Adicionar aos favoritos",
                Child = _heartGlyph,
            };
            _heart.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                ToggleFavorite();
            };
            grid.Children.Add(_heart);

            // Progresso de leitura desta obra, quando existir.
            var hist = LocalStore.Instance.GetHistory(m.Source, m.Id);
            if (hist != null && hist.Position > 0.01)
            {
                var pct = Math.Max(0, Math.Min(1, hist.Position));
                var track = new Grid
                {
                    Height = 4,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
                    IsHitTestVisible = false,
                };
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - pct, GridUnitType.Star) });
                var fill = new Border { Background = Theme.Grad };
                Grid.SetColumn(fill, 0);
                track.Children.Add(fill);
                grid.Children.Add(track);
            }

            _coverBox.Child = grid;
            root.Children.Add(_coverBox);

            _title = new TextBlock
            {
                Text = m.Title,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 38,
                MinHeight = 38,
                Margin = new Thickness(3, 9, 3, 2),
                LineHeight = 17.5,
            };
            root.Children.Add(_title);

            var sub = m.TitleAlt;
            if (showSource)
            {
                var src = SourceRegistry.Instance.NameOf(m.Source);
                var g0 = m.Genres != null && m.Genres.Count > 0 ? m.Genres[0] : null;
                sub = string.IsNullOrWhiteSpace(src) ? (g0 ?? string.Empty) : src + (string.IsNullOrWhiteSpace(g0) ? string.Empty : " · " + g0);
            }
            else if (string.IsNullOrWhiteSpace(sub) && m.Genres != null && m.Genres.Count > 0)
                sub = string.Join(" · ", m.Genres.GetRange(0, Math.Min(2, m.Genres.Count)));
            root.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(sub) ? " " : sub,
                FontSize = 11,
                Foreground = Theme.Muted,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 28,
                MinHeight = 28,
                Margin = new Thickness(3, 0, 3, 3),
            });

            Child = root;

            MouseLeftButtonUp += (_, _) => Clicked?.Invoke(m);
            MouseEnter += (_, _) => SetHover(true);
            MouseLeave += (_, _) => SetHover(false);

            if (autoLoad && !string.IsNullOrEmpty(m.Cover))
                Dispatcher.BeginInvoke(new Action(EnsureCover), DispatcherPriority.Background);
        }

        private void SetHover(bool on)
        {
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(on ? 1.012 : 1.0, TimeSpan.FromMilliseconds(130))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(on ? 1.012 : 1.0, TimeSpan.FromMilliseconds(130))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            _lift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(on ? -3 : 0, TimeSpan.FromMilliseconds(130))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });

            BorderBrush = on ? Theme.AccentLine : Theme.Line;
            Background = on ? Theme.Panel2 : Theme.Panel;
            _sheen.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(on ? 0.55 : 0, TimeSpan.FromMilliseconds(140)));
        }

        private void ToggleFavorite()
        {
            var nowFav = LocalStore.Instance.ToggleFavorite(Manga);
            _heartGlyph.Text = nowFav ? "♥" : "♡";
            _heartGlyph.Foreground = nowFav ? Theme.Accent2 : Theme.Text;
            _heart.ToolTip = nowFav ? "Remover dos favoritos" : "Adicionar aos favoritos";
            if (nowFav)
            {
                _heart.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(220))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        /// <summary>Chamado pelo MangaGrid quando o card entra na faixa visível.</summary>
        public void EnsureCover()
        {
            _coverWanted = true;
            if (_coverLoaded || _coverLoading || string.IsNullOrEmpty(Manga.Cover)) return;

            var cached = ImageLoader.GetCached(Manga.Cover, CoverDecode);
            if (cached != null)
            {
                ApplyCover(cached, false);
                return;
            }

            _coverLoading = true;
            _ = LoadCoverAsync();
        }

        /// <summary>Libera a imagem de cards muito distantes, mantendo o card na grade.</summary>
        public void ReleaseCover()
        {
            _coverWanted = false;
            if (!_coverLoaded) return;

            _coverLoaded = false;
            _img.Source = null;
            _img.Opacity = 0;
            _skeleton.Visibility = Visibility.Visible;
            _letter.Visibility = Visibility.Visible;
        }

        private async System.Threading.Tasks.Task LoadCoverAsync()
        {
            var bmp = await ImageLoader.LoadAsync(Manga.Cover, CoverDecode, RefererFor(Manga.Source)).ConfigureAwait(true);
            if (bmp == null)
            {
                _coverLoading = false;
                return;
            }
            if (!_coverWanted)
            {
                _coverLoading = false;
                return;
            }
            ApplyCover(bmp, true);
        }

        private void ApplyCover(BitmapSource bmp, bool animate)
        {
            _coverLoaded = true;
            _coverLoading = false;
            _img.Source = bmp;
            _skeleton.Visibility = Visibility.Collapsed;
            _letter.Visibility = Visibility.Collapsed;

            if (animate)
            {
                _img.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                _img.Opacity = 1;
            }
        }

        internal static string RefererFor(string source)
        {
            switch (source)
            {
                case "mangalivre": return "https://mangalivre.blog/";
                case "mangaflix": return "https://mangaflix.net/";
                case "mangadex": return "https://mangadex.org/";
                case "mangadex18": return "https://mangadex.org/";
                case "lermanga": return "https://lermangaonline.com.br/";
                case "manhastro": return "https://manhastro.net/";
                case "hanami": return "https://hanamiheaven.org/";
                case "hqbrasa": return "https://hqbrasa.com/";
                case "melhorhentai": return "https://melhorhentai.com/";
                default: return "https://geekstation.com.br/";
            }
        }
    }

    /// <summary>
    /// Grade responsiva de capas.
    /// - Adiciona em lotes por tick da UI para não travar a janela.
    /// - Carrega a capa apenas quando o card chega perto do viewport.
    /// - Libera imagens de cards muito distantes (virtualização de capas),
    ///   mantendo a grade estável e o consumo de memória baixo.
    /// </summary>
    public sealed class MangaGrid : WrapPanel
    {
        private Action<Manga> _onClick;
        private int _generation;
        private ScrollViewer _scroll;
        private DispatcherTimer _viewportTimer;
        private bool _hooked;
        private bool _viewportQueued;

        /// <summary>Mostra a fonte abaixo do título (usado na busca global).</summary>
        public bool ShowSource { get; set; }

        public MangaGrid()
        {
            Orientation = Orientation.Horizontal;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_hooked)
            {
                _scroll = Ui.FindAncestor<ScrollViewer>(this);
                if (_scroll == null) return;

                _hooked = true;
                _scroll.ScrollChanged += OnScrollChanged;
                _scroll.SizeChanged += OnScrollChanged;
                _viewportTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(110),
                };
                _viewportTimer.Tick += (_, _) =>
                {
                    _viewportTimer.Stop();
                    UpdateViewport();
                };
            }
            ScheduleViewport();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _viewportTimer?.Stop();
        }

        private void OnScrollChanged(object sender, EventArgs e)
        {
            if (!IsLoaded) return;
            var timer = _viewportTimer;
            if (timer == null) return;
            timer.Stop();
            timer.Start();
        }

        private void ScheduleViewport()
        {
            if (_viewportQueued) return;
            _viewportQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _viewportQueued = false;
                UpdateViewport();
            }), DispatcherPriority.Background);
        }

        private void UpdateViewport()
        {
            var scroll = _scroll;
            if (scroll == null || !IsLoaded) return;

            var top = scroll.VerticalOffset;
            var bottom = top + Math.Max(1, scroll.ViewportHeight);

            foreach (var child in Children)
            {
                if (!(child is MangaCard card)) continue;

                double y;
                try
                {
                    y = card.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
                }
                catch
                {
                    continue;
                }

                var h = card.ActualHeight > 0 ? card.ActualHeight : 322;
                if (y + h >= top - 500 && y <= bottom + 700)
                {
                    card.EnsureCover();
                }
                else if (y + h < top - 1500 || y > bottom + 2400)
                {
                    card.ReleaseCover();
                }
            }
        }

        public void SetItems(IEnumerable<Manga> items, Action<Manga> onClick = null)
        {
            _onClick = onClick;
            _generation++;
            Children.Clear();
            if (items != null) Append(items, _generation);
            ScheduleViewport();
        }

        public void ClearItems()
        {
            _generation++;
            Children.Clear();
        }

        public void Add(Manga m, Action<Manga> onClick = null)
        {
            var gen = _generation;
            AddCard(m, onClick ?? _onClick, gen);
        }

        /// <summary>Acrescenta mais obras ao final (scroll infinito ou busca progressiva).</summary>
        public void Append(IEnumerable<Manga> items, int gen = -1)
        {
            if (items == null) return;
            var useGen = gen < 0 ? _generation : gen;
            var list = items as IList<Manga> ?? new List<Manga>(items);
            if (list.Count == 0) return;
            AddBatch(list, 0, useGen);
            ScheduleViewport();
        }

        private void AddBatch(IList<Manga> items, int start, int gen)
        {
            const int chunk = 12;
            var end = Math.Min(items.Count, start + chunk);
            for (var i = start; i < end; i++)
            {
                if (gen != _generation) return;
                AddCard(items[i], _onClick, gen);
            }
            if (end >= items.Count) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (gen != _generation) return;
                AddBatch(items, end, gen);
                ScheduleViewport();
            }), DispatcherPriority.Background);
        }

        private void AddCard(Manga m, Action<Manga> onClick, int gen)
        {
            if (gen != _generation || m == null) return;
            var card = new MangaCard(m, ShowSource, false);
            if (onClick != null) card.Clicked += mm => onClick(mm);
            Children.Add(card);
        }

        /// <summary>Esqueleto de carregamento com o mesmo tamanho dos cards reais.</summary>
        public void ShowSkeleton(int count = 12)
        {
            ClearItems();
            for (var i = 0; i < count; i++)
            {
                var box = new Border
                {
                    Width = 172,
                    Height = 322,
                    Margin = new Thickness(0, 0, 16, 22),
                    Background = Theme.Panel,
                    BorderBrush = Theme.Line,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(9),
                    IsHitTestVisible = false,
                };
                var stack = new StackPanel();
                stack.Children.Add(new Border
                {
                    Height = 224,
                    CornerRadius = new CornerRadius(11),
                    Background = Theme.SkeletonGrad,
                });
                stack.Children.Add(new Border
                {
                    Height = 12,
                    Width = 118,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(3, 12, 0, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = Theme.SkeletonGrad,
                });
                stack.Children.Add(new Border
                {
                    Height = 9,
                    Width = 78,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(3, 10, 0, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = Theme.SkeletonGrad,
                });
                box.Child = stack;
                Children.Add(box);
            }
        }
    }

    public static class Ui
    {
        public static Border Panel(UIElement child, double pad = 16)
        {
            return new Border
            {
                Background = Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(pad),
                Child = child,
            };
        }

        public static StackPanel VStack(double gap = 8)
        {
            return new StackPanel { Orientation = Orientation.Vertical };
        }

        public static void Gap(StackPanel sp, double h)
        {
            sp.Children.Add(new Border { Height = h, Background = Brushes.Transparent });
        }

        public static Button Chip(string text, Action click = null, bool active = false)
        {
            var b = new Button
            {
                Content = text,
                Style = (Style)Application.Current.Resources["ChipButton"],
                Background = active ? Theme.AccentSoft : Theme.Panel2,
                Foreground = active ? Theme.Accent2 : Theme.Text,
            };
            if (click != null) b.Click += (_, _) => click();
            return b;
        }

        public static TextBlock Section(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 18, 0, 8),
        };

        public static TextBlock Empty(string text) => new TextBlock
        {
            Text = text,
            Foreground = Theme.Muted,
            FontSize = 14,
            Margin = new Thickness(0, 24, 0, 24),
        };

        public static Border Line()
        {
            return new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 8, 0, 8) };
        }

        public static Border AccentBar(double height = 26, double width = 4)
        {
            return new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(width / 2),
                Background = Theme.Grad,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        /// <summary>Corta um elemento com cantos arredondados, acompanhando o tamanho.</summary>
        public static void RoundClip(FrameworkElement el, double radius)
        {
            if (el == null) return;
            el.SizeChanged += (_, e) =>
            {
                if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
                el.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), radius, radius);
            };
        }

        /// <summary>Sobe na árvore visual até achar o primeiro ancestral do tipo T.</summary>
        public static T FindAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null && !(node is T))
            {
                if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
                    node = VisualTreeHelper.GetParent(node);
                else
                    node = LogicalTreeHelper.GetParent(node);
            }
            return node as T;
        }

        public static ScrollViewer Scroll(UIElement content)
        {
            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content,
                Padding = new Thickness(0),
                PanningMode = PanningMode.VerticalOnly,
            };
            // Só volta ao topo na primeira montagem. Ao retornar pela pilha de
            // navegação, a posição de rolagem da página é preservada.
            var first = true;
            sv.Loaded += (_, _) =>
            {
                if (!first) return;
                first = false;
                sv.ScrollToTop();
            };
            return sv;
        }
    }

    /// <summary>Caixa de busca com placeholder e botão de limpar.</summary>
    public sealed class SearchField : Grid
    {
        public TextBox Box { get; }

        public SearchField(string placeholder, double width = 340)
        {
            Width = width;
            MinWidth = 190;
            MinHeight = 40;
            Box = new TextBox
            {
                ToolTip = placeholder,
                Margin = new Thickness(0),
                Padding = new Thickness(38, 9, 36, 9),
                MinHeight = 40,
            };

            var icon = new TextBlock
            {
                Text = "⌕",
                FontSize = 17,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(14, 0, 0, 0),
                IsHitTestVisible = false,
            };
            var ph = new TextBlock
            {
                Text = placeholder,
                Foreground = Theme.Muted,
                Margin = new Thickness(43, 0, 30, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                FontSize = 13.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var clear = Theme.Btn("✕", "LinkButton");
            clear.Width = 24;
            clear.Height = 24;
            clear.Padding = new Thickness(0);
            clear.FontSize = 11;
            clear.Foreground = Theme.Muted;
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            clear.VerticalAlignment = VerticalAlignment.Center;
            clear.Margin = new Thickness(0, 0, 7, 0);
            clear.ToolTip = "Limpar";
            clear.Focusable = false;
            clear.Visibility = Visibility.Collapsed;
            clear.Click += (_, _) =>
            {
                Box.Text = string.Empty;
                Box.Focus();
            };
            Children.Add(icon);
            Children.Add(Box);
            Children.Add(ph);
            Children.Add(clear);
            Box.TextChanged += (_, _) =>
            {
                var empty = string.IsNullOrEmpty(Box.Text);
                ph.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                clear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            };
        }

        public string Text
        {
            get => Box.Text;
            set => Box.Text = value;
        }
    }
}
