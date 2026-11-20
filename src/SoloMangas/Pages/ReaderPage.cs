using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SoloMangasNative
{
    public sealed class ReaderPage : UserControl
    {
        private readonly MainWindow _host;
        private readonly Manga _manga;
        private readonly ISource _source;
        private readonly List<Chapter> _chapters;
        private readonly Chapter _chapter;
        private Chapter _activeChapter;
        private int _index;

        private readonly Grid _root;
        private readonly Grid _bar;
        private readonly TextBlock _barTitle;
        private readonly TextBlock _barSub;
        private readonly Button _prev;
        private readonly Button _next;
        private readonly Button _continuousBtn;
        private readonly Button _modeBtn;
        private readonly List<Segment> _segments = new List<Segment>();
        private bool _continuous;
        private bool _singlePage;
        private int _widthMode;
        private int _wheelAccum;
        private DateTime _lastPageNav = DateTime.MinValue;
        private bool _loadingNext;
        private bool _switching;
        private string _failedNextId;
        private readonly ScrollViewer _scroll;
        private readonly StackPanel _pagesPanel;
        private readonly TextBlock _zoomLabel;
        private readonly Rectangle _progress;
        private readonly Border _progressTrack;
        private readonly DispatcherTimer _saveTimer;
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly List<PageSlot> _slots = new List<PageSlot>();
        private readonly DispatcherTimer _viewportTimer;
        private double _pageWidth = 820;

        /// <summary>Uma pagina do leitor; a imagem pode ser descarregada quando sai da tela.</summary>
        private sealed class PageSlot
        {
            public Border Holder;
            public Image Image;
            public string Url;
            public int Number;
            public int Total;
            public int PixelW;
            public int PixelH;
            public double FrozenHeight;
            public bool Busy;
        }

        private Grid _drawer;
        private double _zoom = 1.0;
        private double _restoreRatio;
        private bool _restored;
        private bool _savedOnExit;
        private bool _userScrolled;
        private readonly DispatcherTimer _restoreTimer;
        private readonly TextBlock _status;

        /// <summary>Bloco de um capitulo dentro do leitor continuo.</summary>
        private sealed class Segment
        {
            public Chapter Chapter;
            public FrameworkElement Host;
        }

        public ReaderPage(MainWindow host, Manga manga, List<Chapter> chapters, Chapter chapter)
        {
            _host = host;
            _manga = manga;
            _source = SourceRegistry.Instance.Get(manga.Source);
            _chapters = (chapters ?? new List<Chapter>()).ToList();
            _chapter = chapter;
            _index = _chapters.FindIndex(c => c.Id == chapter.Id);
            if (_index < 0)
            {
                _chapters.Add(chapter);
                _chapters.Sort((a, b) => a.Number.CompareTo(b.Number));
                _index = _chapters.IndexOf(chapter);
            }

            _activeChapter = chapter;
            _continuous = LocalStore.Instance.Settings.ContinuousRead;
            _singlePage = LocalStore.Instance.Settings.ReaderMode == "page";
            _widthMode = LocalStore.Instance.Settings.ReaderWidth;

            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ---------------- Barra ----------------
            _bar = new Grid { Margin = new Thickness(0) };
            _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var barBg = new Border
            {
                Background = Theme.Header,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            Grid.SetColumnSpan(barBg, 3);
            _bar.Children.Add(barBg);

            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 8, 10) };
            var back = Theme.Btn("←", "IconButton");
            back.ToolTip = "Voltar à obra (Esc)";
            back.Click += (_, _) => Back();
            left.Children.Add(back);

            var titleStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 520 };
            _barTitle = new TextBlock
            {
                Text = manga.Title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _barSub = new TextBlock { Text = chapter.Title, FontSize = 11.5, Foreground = Theme.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
            titleStack.Children.Add(_barTitle);
            titleStack.Children.Add(_barSub);
            left.Children.Add(titleStack);
            Grid.SetColumn(left, 0);
            _bar.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 10, 14, 10), VerticalAlignment = VerticalAlignment.Center };

            var chaptersBtn = Theme.Btn("☰", "IconButton");
            chaptersBtn.ToolTip = "Capítulos e detalhes da obra (C)";
            chaptersBtn.Click += (_, _) => ToggleDrawer();
            right.Children.Add(chaptersBtn);

            var fullBtn = Theme.Btn("⛶", "IconButton");
            fullBtn.ToolTip = "Tela cheia (F11)";
            fullBtn.Margin = new Thickness(8, 0, 0, 0);
            fullBtn.Click += (_, _) => _host.ToggleFullscreen();
            right.Children.Add(fullBtn);

            right.Children.Add(new Border { Width = 10 });
            var zoomOut = Theme.Btn("A−", "IconButton");
            zoomOut.Click += (_, _) => SetZoom(_zoom - 0.1);
            right.Children.Add(zoomOut);
            _zoomLabel = new TextBlock
            {
                Text = "100%",
                Foreground = Theme.Muted,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            right.Children.Add(_zoomLabel);
            var zoomIn = Theme.Btn("A+", "IconButton");
            zoomIn.Click += (_, _) => SetZoom(_zoom + 0.1);
            right.Children.Add(zoomIn);

            right.Children.Add(new Border { Width = 12 });

            _modeBtn = Theme.Btn("▤", "IconButton");
            _modeBtn.Margin = new Thickness(0, 0, 8, 0);
            _modeBtn.Click += (_, _) => TogglePageMode();
            right.Children.Add(_modeBtn);

            _continuousBtn = Theme.Btn("∞", "IconButton");
            _continuousBtn.Margin = new Thickness(0, 0, 8, 0);
            _continuousBtn.Click += (_, _) => ToggleContinuous();
            right.Children.Add(_continuousBtn);

            right.Children.Add(new Border { Width = 4 });
            _prev = Theme.Btn("‹ Anterior", "GhostButton");
            _prev.Margin = new Thickness(0, 0, 8, 0);
            _prev.Click += (_, _) => Go(-1);
            right.Children.Add(_prev);

            _next = Theme.Btn("Próximo ›", "PrimaryButton");
            _next.Click += (_, _) => Go(1);
            right.Children.Add(_next);

            Grid.SetColumn(right, 2);
            _bar.Children.Add(right);

            _progressTrack = new Border
            {
                Height = 3,
                Background = Theme.Panel2,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _progress = new Rectangle
            {
                Fill = Theme.Grad,
                HorizontalAlignment = HorizontalAlignment.Left,
                Height = 3,
                Width = 0,
            };
            _progressTrack.Child = new Grid { Children = { _progress } };
            Grid.SetColumnSpan(_progressTrack, 3);
            _bar.Children.Add(_progressTrack);

            Grid.SetRow(_bar, 0);
            _root.Children.Add(_bar);

            // ---------------- Paginas ----------------
            _pagesPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 40),
            };
            _pagesPanel.LayoutTransform = _scale;

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Theme.Bg,
                Content = _pagesPanel,
                Padding = new Thickness(0),
            };
            _scroll.ScrollChanged += OnScrollChanged;
            _scroll.SizeChanged += (_, _) => UpdatePageWidths();
            _scroll.PreviewMouseLeftButtonUp += OnReaderClick;
            _scroll.PreviewMouseWheel += (_, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    e.Handled = true;
                    SetZoom(_zoom + (e.Delta > 0 ? 0.1 : -0.1));
                    return;
                }
                // Modo página a página: cada gesto avança/volta uma página.
                if (_singlePage && _slots.Count > 0)
                {
                    e.Handled = true;
                    _userScrolled = true;
                    _wheelAccum += e.Delta;
                    var now = DateTime.UtcNow;
                    if (Math.Abs(_wheelAccum) >= 100 && now - _lastPageNav > TimeSpan.FromMilliseconds(190))
                    {
                        var dir = _wheelAccum > 0 ? -1 : 1;
                        _wheelAccum = 0;
                        _lastPageNav = now;
                        SinglePageStep(dir);
                    }
                    return;
                }
                // Rolagem normal: depois do layout, verifica se chegou ao fim.
                _userScrolled = true;
                Dispatcher.BeginInvoke(new Action(MaybeLoadNextIfNearEnd), DispatcherPriority.Loaded);
            };
            Grid.SetRow(_scroll, 1);
            _root.Children.Add(_scroll);

            _status = new TextBlock
            {
                Text = "Carregando capítulo…",
                Foreground = Theme.Muted,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 0),
            };
            _pagesPanel.Children.Add(_status);

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                SavePosition();
            };

            // Descarrega/recarrega paginas distantes para o leitor continuo nao
            // acumular bitmaps de capitulos inteiros.
            _viewportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _viewportTimer.Tick += (_, _) =>
            {
                _viewportTimer.Stop();
                VirtualizePages();
                UpdateProgress();
                UpdatePageInfo();
            };

            // Reaplica a posicao salva depois que o layout inicial assentou, desde
            // que o usuario ainda nao tenha rolado.
            _restoreTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _restoreTimer.Tick += (_, _) =>
            {
                _restoreTimer.Stop();
                if (!_userScrolled) ApplyRestore();
            };

            Content = _root;
            ApplyContinuousStyle();
            ApplyModeStyle();

            Loaded += (_, _) =>
            {
                var win = Window.GetWindow(this);
                if (win != null) win.PreviewKeyDown += OnKeyDown;
                UpdatePageWidths();
                UpdateNav();
            };
            Unloaded += (_, _) =>
            {
                var win = Window.GetWindow(this);
                if (win != null) win.PreviewKeyDown -= OnKeyDown;
                if (!_savedOnExit) SavePosition();
            };
        }

        private void UpdateNav()
        {
            _prev.IsEnabled = _index > 0;
            _next.IsEnabled = _index >= 0 && _index < _chapters.Count - 1;
            _barTitle.Text = _manga.Title;
            _barSub.Text = _activeChapter.Title;
        }

        public async Task LoadAsync()
        {
            UpdateNav();
            _segments.Clear();
            _slots.Clear();
            _pagesPanel.Children.Clear();
            _pagesPanel.Children.Add(_status);
            _status.Text = "Carregando capítulo…";
            _status.Visibility = Visibility.Visible;

            try
            {
                await AppendChapterAsync(_chapter, true);
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    RestorePosition();
                    VirtualizePages();
                    UpdatePageInfo();
                }), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                _pagesPanel.Children.Clear();
                var box = new StackPanel { Margin = new Thickness(0, 60, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
                box.Children.Add(new TextBlock
                {
                    Text = "Falha ao abrir o capítulo: " + ex.Message,
                    Foreground = Theme.Muted,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 680,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                var retry = Theme.Btn("Tentar de novo", "PrimaryButton");
                retry.HorizontalAlignment = HorizontalAlignment.Center;
                retry.Margin = new Thickness(0, 16, 0, 0);
                retry.Click += (_, _) => { _ = LoadAsync(); };
                box.Children.Add(retry);
                _pagesPanel.Children.Add(box);
            }
        }

        /// <summary>
        /// Acrescenta um capitulo ao fluxo do leitor. O primeiro capitulo substitui o conteudo;
        /// os seguintes entram abaixo, separados por um divisor, formando um capitulo unico.
        /// </summary>
        private async Task AppendChapterAsync(Chapter chapter, bool first)
        {
            var adult = _manga.Adult || LocalStore.Instance.Settings.Adult;
            var pages = await PageSetCache.GetAsync(_source, chapter.Id, adult);

            var slotStart = _slots.Count;
            var host = new StackPanel { Margin = new Thickness(0) };
            if (!first) host.Children.Add(BuildChapterDivider(chapter));

            if (pages.IsText)
            {
                var box = Ui.Panel(new TextBlock
                {
                    Text = pages.Text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 16,
                    LineHeight = 27,
                    Foreground = Theme.Text,
                    MaxWidth = 820,
                }, 26);
                box.MaxWidth = 880;
                host.Children.Add(box);
            }
            else if (pages.Images.Count == 0)
            {
                host.Children.Add(Ui.Empty("Este capítulo não tem páginas disponíveis."));
            }
            else
            {
                for (var i = 0; i < pages.Images.Count; i++)
                {
                    var index = i;
                    var url = pages.Images[i];
                    var holder = new Border
                    {
                        MinHeight = PlaceholderHeight(),
                        Background = Theme.Panel,
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(0, 0, 0, 6),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "Página " + (index + 1) + "…",
                            Foreground = Theme.Muted,
                            FontSize = 12,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    };
                    host.Children.Add(holder);
                    _slots.Add(new PageSlot { Holder = holder, Url = url, Number = index + 1, Total = pages.Images.Count });
                }
            }

            host.Children.Add(BuildChapterFooter(chapter));

            if (first)
            {
                _pagesPanel.Children.Clear();
                _restoreRatio = LocalStore.Instance.GetPosition(_manga.Source, chapter.Id);
            }
            _pagesPanel.Children.Add(host);
            _segments.Add(new Segment { Chapter = chapter, Host = host });
            _ = Dispatcher.BeginInvoke(new Action(VirtualizePages), DispatcherPriority.Background);

            // Começa o download antes do primeiro layout: as primeiras páginas já
            // aparecem quando a janela termina de montar, em vez de esperar o
            // timer de virtualização. No capítulo emendado, aquece só o começo.
            PrimePages(slotStart, first ? 7 : 3);
            PrefetchNextChapter(chapter);

            // Capitulos emendados so contam como lidos/historico quando o leitor entra neles
            // (UpdateActiveSegment); o capitulo inicial entra agora.
            if (first)
            {
                LocalStore.Instance.MarkRead(_manga.Source, chapter.Id);
                UpsertHistory(chapter, _restoreRatio);
            }
            UpdateNav();
        }

        private FrameworkElement BuildChapterDivider(Chapter chapter)
        {
            var grid = new Grid { Margin = new Thickness(0, 26, 0, 18) };
            grid.Children.Add(new Border { Height = 1, Background = Theme.Line, VerticalAlignment = VerticalAlignment.Center });
            var badge = new Border
            {
                Background = Theme.Panel,
                BorderBrush = Theme.Accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(18, 8, 18, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            badge.Child = new TextBlock
            {
                Text = chapter.Title,
                Foreground = Theme.Accent,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            grid.Children.Add(badge);
            return grid;
        }

        /// <summary>
        /// Rodapé de fim de capítulo: deixa claro que o capítulo acabou e oferece
        /// anterior, próximo e a lista completa, sem voltar para a página da obra.
        /// </summary>
        private FrameworkElement BuildChapterFooter(Chapter chapter)
        {
            var index = _chapters.FindIndex(c => c.Id == chapter.Id);
            var box = new StackPanel
            {
                Margin = new Thickness(0, 34, 0, 40),
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 760,
            };

            box.Children.Add(new Border
            {
                Height = 1,
                Width = 360,
                Background = Theme.Line,
                Margin = new Thickness(0, 0, 0, 18),
            });
            box.Children.Add(new TextBlock
            {
                Text = "FIM DO CAPÍTULO",
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            box.Children.Add(new TextBlock
            {
                Text = chapter.Title,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });

            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
            var prev = Theme.Btn("←  Anterior", "GhostButton");
            prev.Margin = new Thickness(0, 0, 8, 8);
            prev.IsEnabled = index > 0;
            if (index > 0) prev.Click += (_, _) => GoToChapter(index - 1);
            actions.Children.Add(prev);

            var list = Theme.Btn("☰  Capítulos", "GhostButton");
            list.Margin = new Thickness(0, 0, 8, 8);
            list.Click += (_, _) => ToggleDrawer();
            actions.Children.Add(list);

            var next = Theme.Btn("Próximo capítulo  →", "PrimaryButton");
            next.Margin = new Thickness(0, 0, 0, 8);
            next.IsEnabled = index >= 0 && index < _chapters.Count - 1;
            if (next.IsEnabled) next.Click += (_, _) => GoToChapter(index + 1);
            actions.Children.Add(next);
            box.Children.Add(actions);

            if (index == _chapters.Count - 1)
                box.Children.Add(new TextBlock
                {
                    Text = "Último capítulo disponível nesta fonte.",
                    FontSize = 11.5,
                    Foreground = Theme.Muted,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0),
                });

            return box;
        }

        private void UpsertHistory(Chapter chapter, double position)
        {
            LocalStore.Instance.UpsertHistory(new HistoryEntry
            {
                Source = _manga.Source,
                MangaId = _manga.Id,
                MangaTitle = _manga.Title,
                Cover = _manga.Cover,
                ChapterId = chapter.Id,
                ChapterTitle = chapter.Title,
                Adult = _manga.Adult,
                Position = position,
                Updated = DateTime.Now,
            });
        }

        private double CurrentPageWidth()
        {
            var viewport = _scroll != null ? _scroll.ViewportWidth : 0;
            if (double.IsNaN(viewport) || viewport <= 0) viewport = ActualWidth;
            if (double.IsNaN(viewport) || viewport <= 0) viewport = 1280;
            var available = Math.Max(420, viewport - (viewport < 760 ? 20 : 64));
            switch (_widthMode)
            {
                case 1: return Math.Min(720, available);
                case 2: return Math.Min(900, available);
                case 3: return Math.Min(1080, available);
                default: return Math.Min(1120, available);
            }
        }

        private double PlaceholderHeight()
        {
            if (_singlePage)
            {
                var vh = _scroll != null ? _scroll.ViewportHeight : 0;
                if (double.IsNaN(vh) || vh <= 0) vh = 800;
                return Math.Max(380, vh * 0.78);
            }
            return Math.Max(380, Math.Round(CurrentPageWidth() * 1.15));
        }

        private double DpiScale()
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                return dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            }
            catch
            {
                return 1.0;
            }
        }

        private void UpdatePageWidths()
        {
            _pageWidth = CurrentPageWidth();
            foreach (var slot in _slots)
            {
                if (slot.Image != null) ApplyPageFit(slot.Image, slot);
                else slot.Holder.MinHeight = PlaceholderHeight();
            }
        }

        /// <summary>
        /// Ajusta a imagem ao modo atual: no webtoon ela ocupa a largura escolhida;
        /// no modo página a página, cabe inteira na tela (quando a proporção é de página).
        /// </summary>
        private void ApplyPageFit(Image img, PageSlot slot)
        {
            if (img == null) return;
            slot.Holder.MaxWidth = double.PositiveInfinity;
            if (_singlePage && slot.PixelW > 0 && slot.PixelH > 0)
            {
                var maxW = Math.Max(360, CurrentPageWidth());
                var vh = _scroll != null ? _scroll.ViewportHeight : 0;
                if (double.IsNaN(vh) || vh <= 0) vh = 800;
                var maxH = Math.Max(320, vh - 24);
                var ratio = (double)slot.PixelH / slot.PixelW;
                if (ratio <= 1.85)
                {
                    var scale = Math.Min(maxW / slot.PixelW, maxH / slot.PixelH);
                    img.Width = Math.Max(120, slot.PixelW * scale);
                    img.Height = Math.Max(160, slot.PixelH * scale);
                    img.Stretch = Stretch.Fill;
                    return;
                }
            }
            img.Width = CurrentPageWidth();
            img.Height = double.NaN;
            img.Stretch = Stretch.Uniform;
        }

        private async Task LoadPageAsync(PageSlot slot)
        {
            if (slot == null || slot.Image != null || slot.Busy) return;
            slot.Busy = true;
            var display = CurrentPageWidth();
            // Acima de 120% de escala o ganho visual é pequeno e o custo de memória
            // cresce muito (páginas altas chegam a 1400x11000).
            var scale = Math.Min(DpiScale(), 1.2);
            var decode = (int)Math.Round(display * scale);
            if (decode < 820) decode = 820;
            if (decode > 1280) decode = 1280;

            BitmapImage bmp = null;
            try
            {
                bmp = await ImageLoader.LoadAsync(slot.Url, decode, MangaCard.RefererFor(_manga.Source));
            }
            finally
            {
                slot.Busy = false;
            }

            if (bmp == null)
            {
                var retry = Theme.Btn("Página " + slot.Number + " falhou — tentar de novo", "GhostButton");
                retry.Click += (_, _) => { _ = LoadPageAsync(slot); };
                slot.Holder.Child = retry;
                return;
            }
            slot.Holder.MinHeight = 0;
            slot.Holder.Height = double.NaN;
            slot.PixelW = bmp.PixelWidth;
            slot.PixelH = bmp.PixelHeight;
            var img = new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            ApplyPageFit(img, slot);
            slot.Image = img;
            slot.Holder.Child = img;
            slot.Holder.Background = Brushes.Transparent;
            _ = Dispatcher.BeginInvoke(new Action(UpdatePageInfo), DispatcherPriority.Background);
        }

        /// <summary>Dispara o download das primeiras páginas de um bloco recém-montado.</summary>
        private void PrimePages(int start, int count)
        {
            if (_slots.Count == 0) return;
            if (start < 0) start = 0;
            var end = Math.Min(_slots.Count, start + Math.Max(1, count));
            for (var i = start; i < end; i++)
            {
                var slot = _slots[i];
                if (slot.Image == null && !slot.Busy) _ = LoadPageAsync(slot);
            }
        }

        /// <summary>
        /// Deixa a lista de páginas do próximo capítulo pronta antes de chegar ao
        /// fim. Só a lista (URLs) é buscada; as imagens continuam sob demanda.
        /// </summary>
        private void PrefetchNextChapter(Chapter chapter)
        {
            var idx = _chapters.FindIndex(c => c.Id == chapter.Id);
            if (idx < 0 || idx >= _chapters.Count - 1) return;
            var next = _chapters[idx + 1];
            var adult = _manga.Adult || LocalStore.Instance.Settings.Adult;
            PageSetCache.Prefetch(_source, next.Id, adult);
        }

        /// <summary>Libera bitmaps de páginas muito distantes e recarrega as que voltam à vista.</summary>
        private void VirtualizePages()
        {
            if (_slots.Count == 0 || _scroll.ViewportHeight <= 0) return;
            var top = _scroll.VerticalOffset;
            var bottom = top + _scroll.ViewportHeight;

            // 1) Descarrega bitmaps bem distantes da tela.
            foreach (var slot in _slots)
            {
                if (slot.Image == null) continue;
                double y;
                try
                {
                    y = slot.Holder.TransformToAncestor(_pagesPanel).Transform(new Point(0, 0)).Y * _zoom;
                }
                catch
                {
                    continue;
                }

                var h = Math.Max(1, slot.Holder.ActualHeight * _zoom);
                var far = y + h < top - 8000 || y > bottom + 8000;
                if (!far) continue;

                slot.FrozenHeight = Math.Max(240, slot.Holder.ActualHeight);
                slot.Holder.Height = slot.FrozenHeight;
                slot.Holder.Child = new TextBlock
                {
                    Text = "Página " + slot.Number + "…",
                    Foreground = Theme.Muted,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                slot.Image.Source = null;
                slot.Image = null;
            }

            // 2) Pré-carrega por índice: 3 páginas atrás e 8 à frente. Depender só
            //    da posição em pixels erra quando o placeholder e a imagem real têm
            //    alturas muito diferentes (tiras de webtoon).
            var current = CurrentSlotIndex();
            var first = Math.Max(0, current - 3);
            var last = Math.Min(_slots.Count - 1, current + 8);
            for (var i = first; i <= last; i++)
            {
                var slot = _slots[i];
                if (slot.Image == null && !slot.Busy) _ = LoadPageAsync(slot);
            }

            // 3) Garante o que está visível mesmo se o índice estiver defasado.
            foreach (var slot in _slots)
            {
                if (slot.Image != null || slot.Busy) continue;
                double y;
                try
                {
                    y = slot.Holder.TransformToAncestor(_pagesPanel).Transform(new Point(0, 0)).Y * _zoom;
                }
                catch
                {
                    continue;
                }

                var h = SlotHeight(slot);
                if (y + h >= top - 400 && y <= bottom + 1200) _ = LoadPageAsync(slot);
            }
        }

        private void RestorePosition()
        {
            if (_restored) return;
            _restored = true;
            if (_restoreRatio <= 0.001 || _segments.Count == 0) return;
            ApplyRestore();
            _restoreTimer.Stop();
            _restoreTimer.Start();
        }

        private void ApplyRestore()
        {
            if (_restoreRatio <= 0.001 || _segments.Count == 0) return;
            var seg = _segments[0];
            var height = SegmentHeight(seg);
            if (height <= 1) return;
            _scroll.ScrollToVerticalOffset(SegmentTop(seg) + height * Math.Min(1, _restoreRatio));
        }

        private double SegmentTop(Segment seg)
        {
            try
            {
                return seg.Host.TransformToAncestor(_pagesPanel).Transform(new Point(0, 0)).Y * _zoom;
            }
            catch
            {
                return 0;
            }
        }

        private double SegmentHeight(Segment seg) => Math.Max(1, seg.Host.ActualHeight * _zoom);

        private Segment ActiveSegment()
        {
            if (_segments.Count == 0) return null;
            var y = _scroll.VerticalOffset + 4;
            var active = _segments[0];
            foreach (var seg in _segments)
            {
                if (SegmentTop(seg) <= y) active = seg;
                else break;
            }
            return active;
        }

        /// <summary>Atualiza capitulo atual conforme a rolagem entra em outro bloco.</summary>
        private void UpdateActiveSegment()
        {
            var seg = ActiveSegment();
            if (seg == null || seg.Chapter.Id == _activeChapter.Id) return;

            var prev = _segments.FirstOrDefault(s => s.Chapter.Id == _activeChapter.Id);
            if (prev != null) LocalStore.Instance.SetPosition(_manga.Source, prev.Chapter.Id, 1.0);

            _activeChapter = seg.Chapter;
            var idx = _chapters.FindIndex(c => c.Id == _activeChapter.Id);
            if (idx >= 0) _index = idx;
            UpdateNav();
            LocalStore.Instance.MarkRead(_manga.Source, _activeChapter.Id);
            UpsertHistory(_activeChapter, 0);
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_scroll.ExtentHeight <= 0) return;
            UpdateProgress();

            UpdateActiveSegment();

            if (e.VerticalChange != 0 || e.ExtentHeightChange != 0)
            {
                _saveTimer.Stop();
                _saveTimer.Start();
            }

            _viewportTimer.Stop();
            _viewportTimer.Start();
        }

        /// <summary>
        /// Leitura continua: so carrega o proximo capitulo quando o usuario pede para
        /// avancar e ja esta perto do fim. Evita encadear capitulos por mudanca de layout.
        /// </summary>
        private void MaybeLoadNextIfNearEnd()
        {
            if (!_continuous || _loadingNext || _segments.Count == 0) return;
            if (_scroll.ExtentHeight <= 0) return;
            var remaining = _scroll.ExtentHeight - (_scroll.VerticalOffset + _scroll.ViewportHeight);
            if (remaining <= 2600) _ = MaybeLoadNextAsync();
        }

        private void SavePosition()
        {
            if (_manga == null) return;
            if (_scroll.ExtentHeight <= 0) return;
            var seg = ActiveSegment();
            if (seg == null) return;
            var top = SegmentTop(seg);
            var height = SegmentHeight(seg);
            var ratio = Math.Min(1, Math.Max(0, (_scroll.VerticalOffset - top) / height));
            LocalStore.Instance.SetPosition(_manga.Source, seg.Chapter.Id, ratio);
            UpsertHistory(seg.Chapter, ratio);
        }

        // ------------------------------------------------------------------
        // Leitura continua (capitulos emendados)
        // ------------------------------------------------------------------
        private void ToggleContinuous()
        {
            _continuous = !_continuous;
            LocalStore.Instance.Settings.ContinuousRead = _continuous;
            LocalStore.Instance.Save();
            ApplyContinuousStyle();

            if (_continuous && _segments.Count > 0)
            {
                var remaining = _scroll.ExtentHeight - (_scroll.VerticalOffset + _scroll.ViewportHeight);
                if (remaining <= 2600) _ = MaybeLoadNextAsync();
            }
        }

        private void ApplyContinuousStyle()
        {
            _continuousBtn.Content = "∞";
            _continuousBtn.ToolTip = _continuous
                ? "Leitura contínua ligada: emenda o próximo capítulo (clique para desligar)"
                : "Leitura contínua desligada (clique para ligar)";
            _continuousBtn.Foreground = _continuous ? Theme.Accent2 : Theme.Text;
            _continuousBtn.BorderBrush = _continuous ? Theme.AccentLine : Theme.Line;
        }

        private void ApplyModeStyle()
        {
            _modeBtn.Content = _singlePage ? "▤" : "≡";
            _modeBtn.ToolTip = _singlePage
                ? "Modo página a página (M) — clique para webtoon"
                : "Modo webtoon (M) — clique para página a página";
            _modeBtn.Foreground = _singlePage ? Theme.Accent2 : Theme.Text;
            _modeBtn.BorderBrush = _singlePage ? Theme.AccentLine : Theme.Line;
        }

        /// <summary>Alterna entre rolagem contínua e avanço de uma página por gesto.</summary>
        private void TogglePageMode()
        {
            _singlePage = !_singlePage;
            LocalStore.Instance.Settings.ReaderMode = _singlePage ? "page" : "webtoon";
            LocalStore.Instance.Save();
            ApplyModeStyle();

            foreach (var slot in _slots)
            {
                if (slot.Image != null) ApplyPageFit(slot.Image, slot);
                else slot.Holder.MinHeight = PlaceholderHeight();
            }
            UpdatePageInfo();

            if (_slots.Count > 0)
            {
                var current = CurrentSlotIndex();
                Dispatcher.BeginInvoke(new Action(() => ScrollToSlot(current)), DispatcherPriority.Loaded);
            }
            _host.Toast(_singlePage
                ? "Modo página a página: clique à esquerda para voltar e à direita para avançar."
                : "Modo webtoon: rolagem contínua.");
        }

        private double SlotTop(PageSlot slot)
        {
            try
            {
                return slot.Holder.TransformToAncestor(_pagesPanel).Transform(new Point(0, 0)).Y * _zoom;
            }
            catch
            {
                return 0;
            }
        }

        private double SlotHeight(PageSlot slot)
        {
            // Placeholder ainda não carregado usa a altura estimada (MinHeight);
            // uma página já descarregada usa a altura congelada para não pular.
            var height = slot.Image == null && slot.FrozenHeight > 0
                ? slot.FrozenHeight
                : slot.Holder.ActualHeight;
            return Math.Max(1, height * _zoom);
        }

        private int CurrentSlotIndex()
        {
            if (_slots.Count == 0) return 0;
            var probe = _scroll.VerticalOffset + Math.Min(_scroll.ViewportHeight * 0.35, 360);
            var best = 0;
            var bestDist = double.MaxValue;
            for (var i = 0; i < _slots.Count; i++)
            {
                var y = SlotTop(_slots[i]);
                var h = SlotHeight(_slots[i]);
                if (probe >= y && probe <= y + h) return i;
                var dist = Math.Abs(probe - y);
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }

        private void ScrollToSlot(int index)
        {
            if (index < 0 || index >= _slots.Count) return;
            var y = SlotTop(_slots[index]);
            _scroll.ScrollToVerticalOffset(Math.Max(0, y - 6));
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (index >= _slots.Count) return;
                var yy = SlotTop(_slots[index]);
                _scroll.ScrollToVerticalOffset(Math.Max(0, yy - 6));
                UpdateProgress();
                UpdatePageInfo();
            }), DispatcherPriority.Loaded);
        }

        private void UpdatePageInfo()
        {
            if (_slots.Count == 0)
            {
                _barSub.Text = _activeChapter.Title;
                return;
            }
            var i = Math.Min(CurrentSlotIndex(), _slots.Count - 1);
            var slot = _slots[i];
            _barSub.Text = _activeChapter.Title + "  ·  página " + slot.Number + "/" + slot.Total;
        }

        /// <summary>Avança/volta exatamente uma página no modo página a página.</summary>
        private void SinglePageStep(int delta)
        {
            if (_slots.Count == 0) return;
            var target = CurrentSlotIndex() + delta;
            if (target < 0)
            {
                if (_index > 0) Go(-1);
                return;
            }
            if (target >= _slots.Count)
            {
                if (_continuous && !_loadingNext)
                {
                    var last = _segments.Count > 0 ? _segments[_segments.Count - 1].Chapter : _activeChapter;
                    var li = _chapters.FindIndex(c => c.Id == last.Id);
                    if (li >= 0 && li < _chapters.Count - 1)
                    {
                        _ = OpenNextAndScrollAsync(_chapters[li + 1], _slots.Count);
                        return;
                    }
                }
                if (_index >= 0 && _index < _chapters.Count - 1) Go(1);
                return;
            }
            ScrollToSlot(target);
        }

        private async Task OpenNextAndScrollAsync(Chapter next, int firstSlot)
        {
            if (_loadingNext) return;
            _loadingNext = true;
            try
            {
                await AppendChapterAsync(next, false);
            }
            catch (Exception ex)
            {
                _host.Toast("Não foi possível carregar o próximo capítulo: " + ex.Message, true);
            }
            finally
            {
                _loadingNext = false;
            }
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                _scroll.UpdateLayout();
                ScrollToSlot(Math.Min(firstSlot, _slots.Count - 1));
            }), DispatcherPriority.Loaded);
        }

        private async Task MaybeLoadNextAsync()
        {
            if (!_continuous || _loadingNext || _segments.Count == 0) return;

            var last = _segments[_segments.Count - 1].Chapter;
            var lastIndex = _chapters.FindIndex(c => c.Id == last.Id);
            if (lastIndex < 0 || lastIndex >= _chapters.Count - 1) return;

            var next = _chapters[lastIndex + 1];
            if (next.Id == _failedNextId) return;

            _loadingNext = true;
            var loading = new TextBlock
            {
                Text = "Carregando " + next.Title + "…",
                Foreground = Theme.Muted,
                FontSize = 12.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 26),
            };
            _pagesPanel.Children.Add(loading);
            try
            {
                await AppendChapterAsync(next, false);
            }
            catch (Exception ex)
            {
                _failedNextId = next.Id;
                _host.Toast("Não foi possível carregar o próximo capítulo: " + ex.Message, true);
            }
            finally
            {
                _pagesPanel.Children.Remove(loading);
                _loadingNext = false;
            }
        }

        private async Task OpenSegmentAsync(Chapter target)
        {
            if (_loadingNext) return;
            _loadingNext = true;
            try
            {
                await AppendChapterAsync(target, false);
                var seg = _segments.FirstOrDefault(s => s.Chapter.Id == target.Id);
                if (seg != null) ScrollToSegment(seg);
            }
            catch (Exception ex)
            {
                _host.Toast("Falha ao abrir capítulo: " + ex.Message, true);
            }
            finally
            {
                _loadingNext = false;
            }
        }

        private void ScrollToSegment(Segment seg)
        {
            var top = SegmentTop(seg);
            _scroll.ScrollToVerticalOffset(Math.Max(0, top - 8));
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var y = SegmentTop(seg);
                _scroll.ScrollToVerticalOffset(Math.Max(0, y - 8));
            }), DispatcherPriority.Loaded);
        }

        private void SetZoom(double value)
        {
            // Preserva o ponto de leitura ao mudar o zoom: guarda o segmento ativo e a
            // fracao visivel dele, aplica a escala e reposiciona depois do novo layout.
            var seg = _segments.Count > 0 ? ActiveSegment() : null;
            var ratio = 0.0;
            if (seg != null)
            {
                var top = SegmentTop(seg);
                var height = SegmentHeight(seg);
                ratio = Math.Min(1, Math.Max(0, (_scroll.VerticalOffset - top) / height));
            }

            _zoom = Math.Max(0.5, Math.Min(2.0, Math.Round(value, 1)));
            _scale.ScaleX = _zoom;
            _scale.ScaleY = _zoom;
            _zoomLabel.Text = (int)Math.Round(_zoom * 100) + "%";

            if (seg == null) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var top = SegmentTop(seg);
                _scroll.ScrollToVerticalOffset(Math.Max(0, top + SegmentHeight(seg) * ratio));
            }), DispatcherPriority.Loaded);
        }

        private void Go(int delta)
        {
            var targetIndex = _index + delta;
            if (targetIndex < 0 || targetIndex >= _chapters.Count) return;
            var target = _chapters[targetIndex];

            if (_continuous)
            {
                var existing = _segments.FirstOrDefault(s => s.Chapter.Id == target.Id);
                if (existing != null)
                {
                    SavePosition();
                    ScrollToSegment(existing);
                    return;
                }
                if (delta > 0 && !_loadingNext)
                {
                    SavePosition();
                    _ = OpenSegmentAsync(target);
                    return;
                }
            }

            GoToChapter(targetIndex);
        }

        /// <summary>Abre outro capítulo dentro do mesmo leitor, preservando a barra e o zoom.</summary>
        private void GoToChapter(int index)
        {
            if (index < 0 || index >= _chapters.Count) return;
            if (_switching) return;
            if (index == _index && string.Equals(_chapters[index].Id, _activeChapter.Id, StringComparison.Ordinal))
            {
                _scroll.ScrollToTop();
                return;
            }
            _ = SwitchToChapterAsync(_chapters[index]);
        }

        private async Task SwitchToChapterAsync(Chapter target)
        {
            if (_switching) return;
            _switching = true;
            try
            {
                SavePosition();

                _segments.Clear();
                _slots.Clear();
                _pagesPanel.Children.Clear();
                _pagesPanel.Children.Add(_status);
                _status.Text = "Carregando " + target.Title + "…";
                _status.Visibility = Visibility.Visible;

                _index = _chapters.FindIndex(c => c.Id == target.Id);
                if (_index < 0)
                {
                    _chapters.Add(target);
                    _chapters.Sort((a, b) => a.Number.CompareTo(b.Number));
                    _index = _chapters.IndexOf(target);
                }
                _activeChapter = target;
                _restored = false;
                _userScrolled = false;
                _restoreRatio = 0;
                _failedNextId = null;
                UpdateNav();

                await AppendChapterAsync(target, true);
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _scroll.UpdateLayout();
                    _scroll.ScrollToTop();
                    RestorePosition();
                    VirtualizePages();
                    UpdatePageInfo();
                    UpdateProgress();
                }), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                _pagesPanel.Children.Clear();
                var box = new StackPanel { Margin = new Thickness(0, 60, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
                box.Children.Add(new TextBlock
                {
                    Text = "Falha ao abrir " + target.Title + ": " + ex.Message,
                    Foreground = Theme.Muted,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 680,
                    TextAlignment = TextAlignment.Center,
                });
                var retry = Theme.Btn("Tentar de novo", "PrimaryButton");
                retry.HorizontalAlignment = HorizontalAlignment.Center;
                retry.Margin = new Thickness(0, 16, 0, 0);
                retry.Click += (_, _) => { _ = SwitchToChapterAsync(target); };
                box.Children.Add(retry);
                _pagesPanel.Children.Add(box);
            }
            finally
            {
                _switching = false;
            }
        }

        private void Back()
        {
            SavePosition();
            _savedOnExit = true;
            if (_host.GoBack()) return;
            _host.ShowManga(_manga.Source, _manga.Id, _manga.Adult);
        }

        /// <summary>
        /// No modo página a página, clicar na faixa esquerda volta uma página e
        /// clicar no restante avança. Botões e barras de rolagem são ignorados.
        /// </summary>
        private void OnReaderClick(object sender, MouseButtonEventArgs e)
        {
            if (!_singlePage || _drawer != null) return;
            if (e.OriginalSource is DependencyObject d &&
                (Ui.FindAncestor<Button>(d) != null || Ui.FindAncestor<ScrollBar>(d) != null)) return;
            var x = e.GetPosition(_scroll).X;
            if (x < 0 || x > _scroll.ActualWidth) return;
            _userScrolled = true;
            SinglePageStep(x < _scroll.ActualWidth * 0.28 ? -1 : 1);
            e.Handled = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                _host.ToggleFullscreen();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                // Em tela cheia, Esc primeiro sai do modo imersivo (não fecha o leitor).
                if (_host.IsFullscreen) { _host.ToggleFullscreen(); e.Handled = true; return; }
                if (_drawer != null) { CloseDrawer(); e.Handled = true; }
                else { Back(); e.Handled = true; }
                return;
            }
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.None)
            {
                ToggleDrawer();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.None)
            {
                TogglePageMode();
                e.Handled = true;
                return;
            }
            if (_drawer != null) return;
            if (e.Key == Key.Left)
            {
                if (_singlePage) SinglePageStep(-1); else Go(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                if (_singlePage) SinglePageStep(1); else Go(1);
                e.Handled = true;
            }
            else if (e.Key == Key.PageDown || e.Key == Key.Space)
            {
                _userScrolled = true;
                if (_singlePage) SinglePageStep(1);
                else
                {
                    _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + _scroll.ViewportHeight * 0.88);
                    QueueContinuousCheck();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.PageUp)
            {
                _userScrolled = true;
                if (_singlePage) SinglePageStep(-1);
                else _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - _scroll.ViewportHeight * 0.88);
                e.Handled = true;
            }
            else if (e.Key == Key.End)
            {
                _userScrolled = true;
                if (_singlePage && _slots.Count > 0) ScrollToSlot(_slots.Count - 1);
                else
                {
                    _scroll.ScrollToEnd();
                    QueueContinuousCheck();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Home)
            {
                _userScrolled = true;
                if (_singlePage && _slots.Count > 0) ScrollToSlot(0);
                else _scroll.ScrollToTop();
                e.Handled = true;
            }
        }

        private void UpdateProgress()
        {
            if (_progressTrack == null || _progressTrack.ActualWidth <= 0) return;
            var max = _scroll.ScrollableHeight;
            var ratio = max <= 0 ? 1.0 : Math.Min(1.0, Math.Max(0.0, _scroll.VerticalOffset / max));
            _progress.Width = _progressTrack.ActualWidth * ratio;
        }

        private void QueueContinuousCheck()
        {
            Dispatcher.BeginInvoke(new Action(MaybeLoadNextIfNearEnd), DispatcherPriority.Loaded);
        }

        // ------------------------------------------------------------------
        // Painel de capitulos (drawer)
        // ------------------------------------------------------------------
        private void ToggleDrawer()
        {
            if (_drawer != null) CloseDrawer();
            else OpenDrawer();
        }

        private void CloseDrawer()
        {
            if (_drawer == null) return;
            _root.Children.Remove(_drawer);
            _drawer = null;
        }

        private void OpenDrawer()
        {
            CloseDrawer();
            var overlay = new Grid();
            var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)) };
            backdrop.MouseLeftButtonDown += (_, e) => { e.Handled = true; CloseDrawer(); };
            overlay.Children.Add(backdrop);

            var panel = new Border
            {
                Width = 390,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1, 0, 0, 0),
            };
            var content = new DockPanel { Margin = new Thickness(18) };

            var close = Theme.Btn("✕", "IconButton");
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Click += (_, _) => CloseDrawer();
            DockPanel.SetDock(close, Dock.Top);
            content.Children.Add(close);

            // cabecalho da obra
            var obra = new Grid { Margin = new Thickness(0, 4, 0, 12) };
            obra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            obra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var coverBox = new Border
            {
                Width = 72,
                Height = 102,
                CornerRadius = new CornerRadius(8),
                Background = Theme.Panel2,
                ClipToBounds = true,
            };
            var cover = new Image { Stretch = Stretch.UniformToFill };
            coverBox.Child = cover;
            if (!string.IsNullOrEmpty(_manga.Cover))
            {
                _ = ImageLoader.LoadAsync(_manga.Cover, 160, MangaCard.RefererFor(_manga.Source))
                    .ContinueWith(t => { if (t.Result != null) Dispatcher.Invoke(() => cover.Source = t.Result); });
            }
            Grid.SetColumn(coverBox, 0);
            obra.Children.Add(coverBox);

            var obraText = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            obraText.Children.Add(new TextBlock
            {
                Text = _manga.Title,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Foreground = Theme.Text,
                TextWrapping = TextWrapping.Wrap,
            });
            obraText.Children.Add(new TextBlock
            {
                Text = _source.Name,
                FontSize = 11,
                Foreground = Theme.Accent,
                Margin = new Thickness(0, 3, 0, 0),
            });
            obraText.Children.Add(new TextBlock
            {
                Text = _activeChapter.Title,
                FontSize = 12,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(obraText, 1);
            obra.Children.Add(obraText);

            DockPanel.SetDock(obra, Dock.Top);
            content.Children.Add(obra);

            if (!string.IsNullOrWhiteSpace(_manga.Description))
            {
                var desc = new TextBlock
                {
                    Text = _manga.Description,
                    FontSize = 12,
                    Foreground = Theme.Muted,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 90,
                    Margin = new Thickness(0, 0, 0, 10),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                DockPanel.SetDock(desc, Dock.Top);
                content.Children.Add(desc);
            }

            var details = Theme.Btn("Ver detalhes da obra", "GhostButton");
            details.HorizontalAlignment = HorizontalAlignment.Left;
            details.Margin = new Thickness(0, 0, 0, 12);
            details.Click += (_, _) =>
            {
                SavePosition();
                _savedOnExit = true;
                _host.ShowManga(_manga.Source, _manga.Id, _manga.Adult);
            };
            DockPanel.SetDock(details, Dock.Top);
            content.Children.Add(details);

            var listTitle = new TextBlock
            {
                Text = "CAPÍTULOS · " + _chapters.Count,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 0, 0, 6),
            };
            DockPanel.SetDock(listTitle, Dock.Top);
            content.Children.Add(listTitle);

            var filterField = new SearchField("Filtrar capítulos…", 338);
            filterField.HorizontalAlignment = HorizontalAlignment.Left;
            filterField.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(filterField, Dock.Top);
            content.Children.Add(filterField);

            var listHost = new StackPanel();
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = listHost,
            };

            var chaptersDesc = _chapters.AsEnumerable().Reverse().ToList();
            var take = 140;
            var start = 0;
            var initialized = false;
            Button currentButton = null;

            // Desenha apenas a fatia visível da lista, centrada no capítulo atual.
            // Obras com 1000+ capítulos abrem o painel na hora, e o filtro acha
            // qualquer capítulo sem precisar montar milhares de botões.
            void RenderDrawer(bool keepWindow)
            {
                var filter = (filterField.Text ?? string.Empty).Trim();
                var filtered = filter.Length == 0
                    ? chaptersDesc
                    : chaptersDesc.Where(c => c.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                listHost.Children.Clear();
                currentButton = null;
                var anchor = false;

                if (filtered.Count == 0)
                {
                    listHost.Children.Add(Ui.Empty("Nenhum capítulo encontrado."));
                    return;
                }

                if (!initialized || !keepWindow)
                {
                    initialized = true;
                    var activePos = filtered.FindIndex(c => c.Id == _activeChapter.Id);
                    start = activePos > 40 ? activePos - 18 : 0;
                    anchor = true;
                }
                if (start >= filtered.Count) start = Math.Max(0, filtered.Count - take);
                if (start < 0) start = 0;

                if (start > 0)
                {
                    var up = Theme.Btn("↑  Mostrar capítulos anteriores", "GhostButton");
                    up.HorizontalAlignment = HorizontalAlignment.Stretch;
                    up.Margin = new Thickness(0, 0, 0, 6);
                    up.Click += (_, _) =>
                    {
                        start = Math.Max(0, start - 120);
                        take += 120;
                        RenderDrawer(true);
                    };
                    listHost.Children.Add(up);
                }

                var slice = filtered.Skip(start).Take(take).ToList();
                foreach (var chapter in slice)
                {
                    var c = chapter;
                    var current = c.Id == _activeChapter.Id;
                    var read = LocalStore.Instance.IsRead(_manga.Source, c.Id);
                    var b = new Button
                    {
                        Style = (Style)Application.Current.Resources["NavButton"],
                        Content = (read ? "✓  " : "    ") + c.Title,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Background = current ? Theme.AccentSoft : Brushes.Transparent,
                        Foreground = current ? Theme.Accent : (read ? Theme.Muted : Theme.Text),
                        FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                        Margin = new Thickness(0, 1, 0, 1),
                        Padding = new Thickness(10, 7, 10, 7),
                    };
                    if (current) currentButton = b;
                    b.Click += (_, _) =>
                    {
                        CloseDrawer();
                        GoToChapter(_chapters.FindIndex(x => x.Id == c.Id));
                    };
                    listHost.Children.Add(b);
                }

                if (start + slice.Count < filtered.Count)
                {
                    var more = Theme.Btn("Mostrar mais (" + (filtered.Count - start - slice.Count) + " restantes)", "GhostButton");
                    more.HorizontalAlignment = HorizontalAlignment.Stretch;
                    more.Margin = new Thickness(0, 8, 0, 4);
                    more.Click += (_, _) =>
                    {
                        take += 160;
                        RenderDrawer(true);
                    };
                    listHost.Children.Add(more);
                }

                var target = currentButton;
                if (anchor && target != null)
                    _ = Dispatcher.BeginInvoke(new Action(() => target.BringIntoView()), DispatcherPriority.Background);
            }

            filterField.Box.TextChanged += (_, _) =>
            {
                initialized = false;
                take = 140;
                RenderDrawer(false);
            };

            RenderDrawer(false);
            content.Children.Add(scroller);

            panel.Child = content;
            overlay.Children.Add(panel);
            _drawer = overlay;
            Grid.SetRow(overlay, 1);
            _root.Children.Add(overlay);

            // animacao de entrada
            var transform = new TranslateTransform(40, 0);
            panel.RenderTransform = transform;
            var anim = new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            transform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }
}
