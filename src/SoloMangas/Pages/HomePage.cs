using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SoloMangasNative
{
    /// <summary>
    /// Início: destaque grande da fonte atual, continuação de leitura de todas as
    /// fontes e catálogo popular da fonte selecionada.
    /// 2.7: cabeçalho fixo com busca sempre à mão, destaque mais compacto,
    /// esqueletos de carregamento e capas carregadas só quando visíveis.
    /// </summary>
    public sealed class HomePage : UserControl, IRefreshable, IPageSource, IAdultAware
    {
        private readonly MainWindow _host;
        private readonly ISource _source;
        private readonly StackPanel _root;
        private readonly StackPanel _heroHost = new StackPanel();
        private readonly StackPanel _continuePanel;
        private readonly MangaGrid _grid;
        private readonly Button _more;
        private readonly Button _refresh;
        private readonly TextBlock _status;
        private readonly TextBlock _continueCount;
        private readonly TextBlock _gridCount;
        private readonly TextBlock _subtitle;
        private readonly ScrollViewer _scroll;
        private readonly SearchField _quickSearch;
        private int _page = 1;
        private int _shown;
        private bool _loading;
        private bool _hasMore;
        private int _loadGen;

        public string PageSource => _source.Key;

        public HomePage(MainWindow host)
        {
            _host = host;
            _source = host.Current;

            _root = new StackPanel { Margin = new Thickness(34, 18, 34, 48) };

            // ---------------- Cabeçalho fixo ----------------
            var header = new Border
            {
                Background = Theme.Header,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(34, 14, 34, 12),
            };

            var headGrid = new Grid();
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Theme.H1("Início"));
            _subtitle = new TextBlock
            {
                Text = SubtitleText(),
                FontSize = 12.5,
                Foreground = Theme.Muted,
                Margin = new Thickness(2, 2, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 420,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            titles.Children.Add(_subtitle);
            Grid.SetColumn(titles, 0);
            headGrid.Children.Add(titles);

            var tools = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _quickSearch = new SearchField("Buscar por título…", 246);
            _quickSearch.Margin = new Thickness(0, 0, 8, 0);
            _quickSearch.Box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                var q = (_quickSearch.Text ?? string.Empty).Trim();
                _host.ShowCatalog(q.Length == 0 ? null : q);
            };
            tools.Children.Add(_quickSearch);

            _refresh = Theme.Btn("Atualizar", "GhostButton");
            _refresh.Height = 40;
            _refresh.MinWidth = 92;
            _refresh.Padding = new Thickness(14, 8, 14, 8);
            _refresh.FontSize = 12.5;
            _refresh.VerticalAlignment = VerticalAlignment.Center;
            _refresh.ToolTip = "Recarregar o destaque e o catálogo desta fonte";
            _refresh.Click += (_, _) => _ = LoadAsync();
            tools.Children.Add(_refresh);

            Grid.SetColumn(tools, 1);
            headGrid.Children.Add(tools);
            header.Child = headGrid;

            // ---------------- Conteúdo rolável ----------------
            _root.Children.Add(_heroHost);

            var contHead = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
            var contTitle = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            contTitle.Children.Add(Ui.AccentBar(22, 4));
            contTitle.Children.Add(new TextBlock
            {
                Text = "CONTINUAR LENDO",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            _continueCount = new TextBlock
            {
                FontSize = 11,
                Foreground = Theme.Muted,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            contTitle.Children.Add(_continueCount);
            DockPanel.SetDock(contTitle, Dock.Left);
            contHead.Children.Add(contTitle);

            var historyLink = Theme.Btn("Ver histórico →", "LinkButton");
            historyLink.HorizontalAlignment = HorizontalAlignment.Right;
            historyLink.VerticalAlignment = VerticalAlignment.Center;
            historyLink.Click += (_, _) => _host.ShowHistory();
            DockPanel.SetDock(historyLink, Dock.Right);
            contHead.Children.Add(historyLink);
            _root.Children.Add(contHead);

            _continuePanel = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
            _root.Children.Add(_continuePanel);

            var exploreHead = new DockPanel { Margin = new Thickness(0, 22, 0, 0), LastChildFill = false };
            var exploreTitle = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            exploreTitle.Children.Add(Ui.AccentBar(22, 4));
            exploreTitle.Children.Add(new TextBlock
            {
                Text = "POPULARES EM " + _source.Name.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            _gridCount = new TextBlock
            {
                FontSize = 11,
                Foreground = Theme.Muted,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            exploreTitle.Children.Add(_gridCount);
            DockPanel.SetDock(exploreTitle, Dock.Left);
            exploreHead.Children.Add(exploreTitle);

            var catalogLink = Theme.Btn("Abrir catálogo →", "LinkButton");
            catalogLink.HorizontalAlignment = HorizontalAlignment.Right;
            catalogLink.VerticalAlignment = VerticalAlignment.Center;
            catalogLink.Click += (_, _) => _host.ShowCatalog();
            DockPanel.SetDock(catalogLink, Dock.Right);
            exploreHead.Children.Add(catalogLink);
            _root.Children.Add(exploreHead);

            _status = new TextBlock
            {
                Foreground = Theme.Muted,
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 10),
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap,
            };
            _root.Children.Add(_status);

            _grid = new MangaGrid { Margin = new Thickness(0, 10, 0, 0) };
            _root.Children.Add(_grid);

            _more = Theme.Btn("Carregar mais", "GhostButton");
            _more.HorizontalAlignment = HorizontalAlignment.Center;
            _more.Margin = new Thickness(0, 14, 0, 0);
            _more.Visibility = Visibility.Collapsed;
            _more.Click += async (_, _) => await LoadMoreAsync();
            _root.Children.Add(_more);

            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.Children.Add(header);
            _scroll = Ui.Scroll(_root);
            Grid.SetRow(_scroll, 1);
            shell.Children.Add(_scroll);

            Content = shell;
            _scroll.ScrollChanged += OnScrollChanged;
        }

        private string SubtitleText()
        {
            var count = SourceRegistry.Instance.Enabled(LocalStore.Instance.Settings.Adult).Count();
            return "Explorando " + _source.Name + " · " + count + (count == 1 ? " fonte" : " fontes");
        }

        /// <summary>Modo +18 mudou: recarrega o destaque e o catálogo popular da fonte.</summary>
        public void AdultChanged()
        {
            _subtitle.Text = SubtitleText();
            _ = LoadAsync();
        }

        // Modo infinito: ao chegar perto do fim, carrega a próxima página automaticamente.
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!IsLoaded || !IsVisible) return;
            if (!_hasMore || _loading) return;
            if (e.ExtentHeight <= 0 || e.ViewportHeight <= 1) return;
            var remaining = e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight);
            if (remaining <= 900) _ = LoadMoreAsync();
        }

        // ------------------------------------------------------------------
        // Continuar lendo (todas as fontes)
        // ------------------------------------------------------------------
        private void RenderContinue()
        {
            _continuePanel.Children.Clear();
            var entries = LocalStore.Instance.History
                .Where(h => !string.IsNullOrWhiteSpace(h.MangaId))
                .Where(h => SourceRegistry.Instance.Has(h.Source))
                .Take(8)
                .ToList();

            _continueCount.Text = entries.Count == 0 ? string.Empty : entries.Count + " em andamento";
            if (entries.Count == 0)
            {
                _continuePanel.Children.Add(new Border
                {
                    Background = Theme.Panel,
                    BorderBrush = Theme.Line,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(18, 16, 18, 16),
                    Margin = new Thickness(0, 2, 0, 0),
                    Child = new TextBlock
                    {
                        Text = "Nada em andamento. Abra uma obra e comece a ler — ela aparece aqui, de qualquer fonte.",
                        Foreground = Theme.Muted,
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                    },
                });
                return;
            }

            var wrap = new WrapPanel();
            foreach (var h in entries)
                wrap.Children.Add(BuildContinueCard(h));
            _continuePanel.Children.Add(wrap);
        }

        private Border BuildContinueCard(HistoryEntry entry)
        {
            var card = new Border
            {
                Width = 304,
                Height = 96,
                Margin = new Thickness(0, 0, 14, 14),
                Background = Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(9),
                Cursor = Cursors.Hand,
                ToolTip = "Continuar " + entry.MangaTitle + " — " + entry.ChapterTitle,
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var coverBox = new Border
            {
                Width = 56,
                Height = 78,
                CornerRadius = new CornerRadius(8),
                Background = Theme.Panel2,
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Ui.RoundClip(coverBox, 8);
            var img = new Image { Stretch = Stretch.UniformToFill };
            coverBox.Child = img;
            if (!string.IsNullOrEmpty(entry.Cover))
                _ = LoadImageAsync(img, entry.Cover, entry.Source, 140, 1.0);
            Grid.SetColumn(coverBox, 0);
            row.Children.Add(coverBox);

            var texts = new StackPanel { Margin = new Thickness(11, 3, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = entry.MangaTitle,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 202,
            });
            texts.Children.Add(new TextBlock
            {
                Text = entry.ChapterTitle,
                FontSize = 11.5,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 202,
            });

            var meta = new TextBlock
            {
                Text = SourceRegistry.Instance.NameOf(entry.Source) + "  ·  " + Ago(entry.Updated),
                FontSize = 10.5,
                Foreground = Theme.Accent2,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 202,
            };
            texts.Children.Add(meta);

            var pct = Math.Max(0, Math.Min(1, entry.Position));
            var barRow = new Grid { Margin = new Thickness(0, 6, 0, 0), Width = 202, HorizontalAlignment = HorizontalAlignment.Left };
            barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var track = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = Theme.Line,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var trackGrid = new Grid();
            trackGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            trackGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - pct, GridUnitType.Star) });
            var fill = new Border { Background = Theme.Grad, CornerRadius = new CornerRadius(2) };
            Grid.SetColumn(fill, 0);
            trackGrid.Children.Add(fill);
            track.Child = trackGrid;
            Grid.SetColumn(track, 0);
            barRow.Children.Add(track);

            var pctText = new TextBlock
            {
                Text = "  " + (int)Math.Round(pct * 100) + "%",
                FontSize = 10,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(pctText, 1);
            barRow.Children.Add(pctText);
            texts.Children.Add(barRow);

            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);

            card.Child = row;
            card.MouseEnter += (_, _) =>
            {
                card.Background = Theme.Panel2;
                card.BorderBrush = Theme.AccentLine;
            };
            card.MouseLeave += (_, _) =>
            {
                card.Background = Theme.Panel;
                card.BorderBrush = Theme.Line;
            };
            card.MouseLeftButtonUp += (_, _) =>
                _host.ShowManga(entry.Source, entry.MangaId, entry.Adult, entry.ChapterId);
            return card;
        }

        private static string Ago(DateTime when)
        {
            var span = DateTime.Now - when;
            if (span.TotalMinutes < 1) return "agora";
            if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " min";
            if (span.TotalHours < 24) return (int)span.TotalHours + " h";
            if (span.TotalDays < 7) return (int)span.TotalDays + " d";
            return when.ToString("dd/MM/yyyy");
        }

        // ------------------------------------------------------------------
        // Destaque
        // ------------------------------------------------------------------
        private void ShowHeroSkeleton()
        {
            _heroHost.Children.Clear();

            var box = new Border
            {
                Height = 322,
                Margin = new Thickness(0, 18, 0, 6),
                CornerRadius = new CornerRadius(20),
                Background = Theme.GradPanel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border Block(double w, double h, double top, double radius = 6) => new Border
            {
                Width = w,
                Height = h,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, top, 0, 0),
                CornerRadius = new CornerRadius(radius),
                Background = Theme.SkeletonGrad,
            };

            var left = new StackPanel
            {
                Margin = new Thickness(32, 28, 24, 28),
                VerticalAlignment = VerticalAlignment.Center,
            };
            left.Children.Add(Block(78, 12, 0, 6));
            left.Children.Add(Block(240, 30, 14, 6));
            left.Children.Add(Block(180, 30, 8, 6));
            left.Children.Add(Block(268, 9, 20, 4));
            left.Children.Add(Block(224, 9, 7, 4));
            left.Children.Add(Block(138, 34, 22, 9));
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var right = new Border { Background = Theme.SkeletonGrad };
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            box.Child = grid;
            _heroHost.Children.Add(box);
        }

        private void ShowHero(Manga m)
        {
            _heroHost.Children.Clear();
            if (m == null) return;

            var outer = new Border
            {
                Height = 322,
                Margin = new Thickness(0, 18, 0, 6),
                CornerRadius = new CornerRadius(20),
                Background = Theme.GradPanel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "Abrir " + m.Title,
            };
            outer.SizeChanged += (_, e) =>
            {
                if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
                outer.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 20, 20);
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // capa à direita, com zoom sutil no hover
            var coverBox = new Border
            {
                Background = Theme.Panel2,
                ClipToBounds = true,
            };
            Ui.RoundClip(coverBox, 20);
            var cover = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            var coverScale = new ScaleTransform(1, 1);
            cover.RenderTransformOrigin = new Point(0.5, 0.5);
            cover.RenderTransform = coverScale;
            coverBox.Child = cover;
            Grid.SetColumn(coverBox, 1);
            grid.Children.Add(coverBox);

            if (!string.IsNullOrEmpty(m.Cover))
                _ = LoadImageAsync(cover, m.Cover, m.Source, 760, 0.96);

            // véu escuro da esquerda para a direita + base
            var shade = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop(Theme.Shade(0xFB), 0),
                        new GradientStop(Theme.Shade(0xEE), 0.38),
                        new GradientStop(Theme.Shade(0x99), 0.62),
                        new GradientStop(Theme.Shade(0x22), 0.86),
                        new GradientStop(Theme.Shade(0x00), 1),
                    },
                },
                IsHitTestVisible = false,
            };
            grid.Children.Add(shade);

            var bottom = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop(Theme.Shade(0x00), 0),
                        new GradientStop(Theme.Shade(0xAA), 1),
                    },
                },
                IsHitTestVisible = false,
            };
            grid.Children.Add(bottom);

            var content = new StackPanel
            {
                Margin = new Thickness(32, 26, 26, 26),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            var eye = new StackPanel { Orientation = Orientation.Horizontal };
            eye.Children.Add(new Border
            {
                Background = Theme.AccentSoft,
                BorderBrush = Theme.AccentLine,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(10, 4, 10, 4),
                Child = new TextBlock
                {
                    Text = "EM DESTAQUE",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Theme.Accent2,
                },
            });
            eye.Children.Add(new TextBlock
            {
                Text = "  " + _source.Name,
                FontSize = 11,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(eye);

            content.Children.Add(new TextBlock
            {
                Text = m.Title,
                FontSize = 35,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = Theme.Text,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 39,
                MaxHeight = 82,
                Margin = new Thickness(0, 10, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var desc = Clean(m.Description);
            if (desc.Length > 0)
            {
                if (desc.Length > 220)
                {
                    var cut = desc.LastIndexOf(' ', 219);
                    if (cut < 150) cut = 219;
                    desc = desc.Substring(0, cut).TrimEnd() + "…";
                }
                content.Children.Add(new TextBlock
                {
                    Text = desc,
                    FontSize = 13,
                    Foreground = Theme.Muted,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20,
                    MaxHeight = 60,
                    Margin = new Thickness(0, 8, 0, 0),
                });
            }

            var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            void AddChip(string text)
            {
                chips.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(9, 4, 9, 4),
                    Margin = new Thickness(0, 0, 7, 6),
                    Child = new TextBlock
                    {
                        Text = text,
                        FontSize = 11,
                        Foreground = Theme.Text,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 220,
                    },
                });
            }
            if (m.Chapters > 0) AddChip(m.Chapters + " capítulos");
            if (!string.IsNullOrWhiteSpace(m.Status)) AddChip(m.Status);
            if (m.Year > 0) AddChip(m.Year.ToString());
            if (m.Adult) AddChip("+18");
            if (!string.IsNullOrWhiteSpace(m.Latest)) AddChip(m.Latest);
            if (m.Genres != null)
                foreach (var g in m.Genres.Take(2)) AddChip(g);
            if (chips.Children.Count > 0) content.Children.Add(chips);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var hist = LocalStore.Instance.GetHistory(m.Source, m.Id);
            var read = Theme.Btn(hist == null ? "▶  Abrir obra" : "▶  Continuar leitura", "PrimaryButton");
            read.Margin = new Thickness(0, 0, 10, 0);
            read.Click += (_, _) => OpenHero(m);
            actions.Children.Add(read);

            var details = Theme.Btn("Ver detalhes", "GhostButton");
            details.Click += (_, _) => _host.ShowManga(m);
            actions.Children.Add(details);
            content.Children.Add(actions);

            Grid.SetColumn(content, 0);
            grid.Children.Add(content);
            outer.Child = grid;

            outer.MouseEnter += (_, _) =>
            {
                outer.BorderBrush = Theme.AccentLine;
                coverScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(1.035, TimeSpan.FromMilliseconds(260))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                coverScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(1.035, TimeSpan.FromMilliseconds(260))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            };
            outer.MouseLeave += (_, _) =>
            {
                outer.BorderBrush = Theme.Line;
                coverScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                coverScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            };

            outer.MouseLeftButtonUp += (_, e) =>
            {
                // Botões tratam o próprio clique; o resto do destaque abre a obra.
                if (e.OriginalSource is DependencyObject src && Ui.FindAncestor<Button>(src) != null) return;
                OpenHero(m);
            };

            _heroHost.Children.Add(outer);
        }

        private static string Clean(string value) =>
            System.Text.RegularExpressions.Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim();

        private void OpenHero(Manga m)
        {
            var hist = LocalStore.Instance.GetHistory(m.Source, m.Id);
            if (hist != null && !string.IsNullOrWhiteSpace(hist.ChapterId))
                _host.ShowManga(m.Source, m.Id, m.Adult, hist.ChapterId);
            else
                _host.ShowManga(m);
        }

        // ------------------------------------------------------------------
        // Imagens
        // ------------------------------------------------------------------
        private async Task LoadImageAsync(Image img, string url, string source, int decode, double targetOpacity)
        {
            if (img == null || string.IsNullOrEmpty(url)) return;

            var cached = ImageLoader.GetCached(url, decode);
            if (cached != null)
            {
                img.Source = cached;
                img.Opacity = targetOpacity;
                return;
            }

            var bmp = await ImageLoader.LoadAsync(url, decode, MangaCard.RefererFor(source)).ConfigureAwait(true);
            if (bmp == null) return;
            img.Source = bmp;
            img.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, targetOpacity, TimeSpan.FromMilliseconds(240))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }

        // ------------------------------------------------------------------
        // Catálogo
        // ------------------------------------------------------------------
        public async Task LoadAsync()
        {
            if (_loading) return;
            _loading = true;
            var gen = ++_loadGen;
            UpdateHeader();
            RenderContinue();
            ShowHeroSkeleton();
            _grid.ShowSkeleton(12);
            _hasMore = false;
            _shown = 0;
            _gridCount.Text = string.Empty;
            _scroll.ScrollToTop();
            _more.Visibility = Visibility.Collapsed;
            _status.Foreground = Theme.Muted;
            _status.Visibility = Visibility.Visible;
            _status.Text = "Buscando destaque e populares de " + _source.Name + "…";
            _page = 1;
            Log.Write("home: catalogo inicio " + _source.Key);
            try
            {
                var result = await _source.CatalogAsync(new SourceQuery
                {
                    Page = 1,
                    Limit = 32,
                    Adult = LocalStore.Instance.Settings.Adult,
                });
                if (gen != _loadGen) return;

                var hero = result.Items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Cover)) ?? result.Items.FirstOrDefault();
                ShowHero(hero);

                _grid.SetItems(result.Items, OpenManga);
                _shown = result.Items.Count;
                _gridCount.Text = _shown == 0 ? string.Empty : _shown + " obras";
                Log.Write("home: cards ok " + result.Items.Count);
                _status.Visibility = result.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (result.Items.Count == 0) _status.Text = "Nenhuma obra encontrada nesta fonte.";
                _hasMore = result.HasMore && result.Items.Count > 0;
                _more.Visibility = _hasMore ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (gen != _loadGen) return;
                Log.Write("home: erro " + ex);
                ShowHero(null);
                _grid.ClearItems();
                _status.Visibility = Visibility.Visible;
                _status.Foreground = Theme.Accent2;
                _status.Text = "Falha ao carregar: " + ex.Message;
            }
            finally
            {
                if (gen == _loadGen)
                {
                    _loading = false;
                    AutoFill();
                }
            }
        }

        private void UpdateHeader()
        {
            _subtitle.Text = SubtitleText();
        }

        /// <summary>Atualiza a faixa "Continuar lendo" ao voltar do leitor.</summary>
        public void Refresh() => RenderContinue();

        // Se a primeira página não preencheu a tela, já busca a próxima.
        private void AutoFill()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded || !IsVisible) return;
                if (!_hasMore || _loading) return;
                if (_scroll.ViewportHeight <= 1) return;
                if (_scroll.ExtentHeight <= _scroll.ViewportHeight + 4) _ = LoadMoreAsync();
            }));
        }

        private async Task LoadMoreAsync()
        {
            if (_loading || !_hasMore) return;
            _loading = true;
            var gen = _loadGen;
            _more.IsEnabled = false;
            _more.Content = "Carregando…";
            try
            {
                var nextPage = _page + 1;
                var result = await _source.CatalogAsync(new SourceQuery
                {
                    Page = nextPage,
                    Limit = 32,
                    Adult = LocalStore.Instance.Settings.Adult,
                });
                if (gen != _loadGen) return;
                _page = nextPage;
                _grid.Append(result.Items);
                _shown += result.Items.Count;
                _gridCount.Text = _shown + " obras";
                Log.Write("home: pagina " + _page + " +" + result.Items.Count + " hasMore=" + result.HasMore);
                _hasMore = result.HasMore && result.Items.Count > 0;
                _more.Visibility = _hasMore ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (gen != _loadGen) return;
                _hasMore = false;
                _host.Toast(ex.Message, true);
            }
            finally
            {
                if (gen == _loadGen)
                {
                    _more.IsEnabled = true;
                    _more.Content = "Carregar mais";
                    _loading = false;
                    AutoFill();
                }
            }
        }

        private void OpenManga(Manga m) => _host.ShowManga(m);
    }
}
