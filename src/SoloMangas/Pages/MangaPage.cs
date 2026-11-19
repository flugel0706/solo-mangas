using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SoloMangasNative
{
    public sealed class MangaPage : UserControl, IRefreshable, IPageSource
    {
        private readonly MainWindow _host;
        private readonly ISource _source;
        private readonly string _id;
        private readonly bool _adultHint;
        private readonly string _openChapterId;

        private readonly StackPanel _root;
        private readonly StackPanel _info;
        private readonly Border _coverBox;
        private readonly Image _cover;
        private readonly TextBlock _coverLetter;
        private readonly StackPanel _chapterPanel;
        private readonly TextBlock _status;
        private readonly TextBlock _chapterCount;
        private readonly TextBox _filter;
        private readonly Button _orderBtn;
        private readonly DispatcherTimer _filterTimer;
        private Button _favBtn;
        private Button _readBtn;

        private Manga _manga;
        private List<Chapter> _chapters = new List<Chapter>();
        private bool _descOrder = true;
        private int _renderGen;
        private WrapPanel _wrap;
        private List<Chapter> _renderList = new List<Chapter>();
        private int _renderShown;
        private Button _showMore;
        private string _resumeChapterId = string.Empty;

        public string PageSource => _source.Key;

        public MangaPage(MainWindow host, string source, string id, bool adult, string openChapterId = null)
        {
            _host = host;
            _source = SourceRegistry.Instance.Get(source);
            _id = id;
            _adultHint = adult;
            _openChapterId = openChapterId;
            _descOrder = LocalStore.Instance.Settings.ChapterOrderDesc;

            _root = new StackPanel { Margin = new Thickness(34, 18, 34, 46) };

            var back = Theme.Btn("←  Voltar", "GhostButton");
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 0, 0, 18);
            back.Click += (_, _) =>
            {
                if (!_host.GoBack()) _host.ShowHome();
            };
            _root.Children.Add(back);

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _coverBox = new Border
            {
                Width = 242,
                Height = 336,
                CornerRadius = new CornerRadius(16),
                Background = Theme.Panel2,
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 26, 20),
            };
            Ui.RoundClip(_coverBox, 16);
            var coverGrid = new Grid();
            coverGrid.Children.Add(new Border
            {
                Background = Theme.GradPanel,
            });
            // Letra grande enquanto a capa não chega (ou quando a fonte está fora).
            _coverLetter = new TextBlock
            {
                Text = string.Empty,
                FontSize = 96,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = Theme.AccentSoft,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            coverGrid.Children.Add(_coverLetter);
            _cover = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            coverGrid.Children.Add(_cover);
            coverGrid.Children.Add(new Border
            {
                Height = 1,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false,
            });
            _coverBox.Child = coverGrid;
            Grid.SetColumn(_coverBox, 0);
            head.Children.Add(_coverBox);

            _info = new StackPanel();
            Grid.SetColumn(_info, 1);
            head.Children.Add(_info);

            _root.Children.Add(head);

            _status = new TextBlock { Foreground = Theme.Muted, FontSize = 13, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
            _root.Children.Add(_status);

            // ---------------- capítulos ----------------
            var chapHead = new DockPanel { Margin = new Thickness(0, 14, 0, 12) };
            var chTitles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            chTitles.Children.Add(Ui.AccentBar(22, 4));
            chTitles.Children.Add(new TextBlock
            {
                Text = "Capítulos",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = Theme.Text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(9, 0, 0, 0),
            });
            _chapterCount = new TextBlock { Foreground = Theme.Muted, FontSize = 12.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            chTitles.Children.Add(_chapterCount);
            DockPanel.SetDock(chTitles, Dock.Left);
            chapHead.Children.Add(chTitles);

            var tools = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var filterField = new SearchField("Filtrar capítulos…", 230) { Margin = new Thickness(0, 0, 8, 0) };
            _filter = filterField.Box;
            tools.Children.Add(filterField);
            _orderBtn = Theme.Btn("Ordem: último → 1º", "GhostButton");
            _orderBtn.ToolTip = "Alternar a ordem dos capítulos";
            _orderBtn.Click += (_, _) =>
            {
                _descOrder = !_descOrder;
                _orderBtn.Content = _descOrder ? "Ordem: último → 1º" : "Ordem: 1º → último";
                RenderChapters();
            };
            tools.Children.Add(_orderBtn);
            DockPanel.SetDock(tools, Dock.Right);
            chapHead.Children.Add(tools);
            _root.Children.Add(chapHead);

            _chapterPanel = new StackPanel();
            _root.Children.Add(_chapterPanel);

            Content = Ui.Scroll(_root);

            // Filtro com debounce: só redesenha depois da pausa de digitação.
            _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(190) };
            _filterTimer.Tick += (_, _) =>
            {
                _filterTimer.Stop();
                RenderChapters();
            };
            _filter.TextChanged += (_, _) =>
            {
                _filterTimer.Stop();
                _filterTimer.Start();
            };
        }

        public async Task LoadAsync()
        {
            _status.Foreground = Theme.Muted;
            _status.Text = "Carregando obra…";

            var adult = _adultHint || LocalStore.Instance.Settings.Adult;
            Manga manga = null;
            string mangaError = null;
            string chapterError = null;

            // Detalhe e capítulos em paralelo, mas independentes: uma falha na
            // lista de capítulos não impede abrir a obra (e vice-versa).
            var mangaTask = _source.MangaAsync(_id, adult);
            var chaptersTask = _source.ChaptersAsync(_id);

            try { manga = await mangaTask.ConfigureAwait(true); }
            catch (Exception ex) { mangaError = ex.Message; }

            try
            {
                var raw = await chaptersTask.ConfigureAwait(true);
                _chapters = ChapterOrder.Ascending(raw);
            }
            catch (Exception ex)
            {
                _chapters = new List<Chapter>();
                chapterError = ex.Message;
            }

            if (manga == null)
            {
                _status.Foreground = Theme.Accent2;
                _status.Text = "Falha ao abrir a obra: " + (mangaError ?? "não encontrada");
                return;
            }

            _manga = manga;
            _manga.Source = _source.Key;
            if (string.IsNullOrEmpty(_manga.Id)) _manga.Id = _id;
            if (_manga.Chapters < _chapters.Count) _manga.Chapters = _chapters.Count;

            var hist = LocalStore.Instance.GetHistory(_manga.Source, _manga.Id);
            _resumeChapterId = hist != null && _chapters.Any(c => c.Id == hist.ChapterId) ? hist.ChapterId : string.Empty;

            RenderInfo();
            RenderChapters();
            _status.Foreground = Theme.Muted;
            _status.Text = chapterError == null ? string.Empty : "Não foi possível carregar os capítulos: " + chapterError;
            if (chapterError != null) _host.Toast("Capítulos indisponíveis nesta fonte agora.", true);

            if (!string.IsNullOrEmpty(_openChapterId))
            {
                var target = _chapters.FirstOrDefault(c => c.Id == _openChapterId);
                if (target != null)
                {
                    OpenChapter(target);
                    return;
                }
            }
        }

        /// <summary>Atualiza lidos/capítulo atual quando o usuário volta do leitor.</summary>
        public void Refresh()
        {
            if (_manga == null) return;
            var hist = LocalStore.Instance.GetHistory(_manga.Source, _manga.Id);
            _resumeChapterId = hist != null && _chapters.Any(c => c.Id == hist.ChapterId) ? hist.ChapterId : string.Empty;
            RenderInfo();
            RenderChapters();
        }

        private void RenderInfo()
        {
            _info.Children.Clear();
            if (_coverLetter != null)
                _coverLetter.Text = string.IsNullOrWhiteSpace(_manga.Title)
                    ? "?"
                    : _manga.Title.Trim().Substring(0, 1).ToUpperInvariant();
            _info.Children.Add(new TextBlock
            {
                Text = _manga.Title,
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = Theme.Text,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 38,
            });
            if (!string.IsNullOrWhiteSpace(_manga.TitleAlt))
                _info.Children.Add(new TextBlock
                {
                    Text = _manga.TitleAlt,
                    FontSize = 13.5,
                    Foreground = Theme.Muted,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });

            var meta = new WrapPanel { Margin = new Thickness(0, 14, 0, 8) };
            void AddMeta(string label, string value)
            {
                var b = new Border
                {
                    Background = Theme.Panel2,
                    BorderBrush = Theme.Line,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(11, 5, 11, 5),
                    Margin = new Thickness(0, 0, 8, 8),
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = label + "  ", FontSize = 11.5, Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = value, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, VerticalAlignment = VerticalAlignment.Center });
                b.Child = sp;
                meta.Children.Add(b);
            }

            AddMeta("Capítulos", (_manga.Chapters > 0 ? _manga.Chapters : _chapters.Count).ToString());
            if (!string.IsNullOrWhiteSpace(_manga.Status)) AddMeta("Status", _manga.Status);
            if (_manga.Year > 0) AddMeta("Ano", _manga.Year.ToString());
            AddMeta("Fonte", _source.Name);

            var readCount = LocalStore.Instance.ReadCount(_manga.Source, _chapters);
            AddMeta("Lidos", readCount + "/" + _chapters.Count);
            _info.Children.Add(meta);

            if (_chapters.Count > 0)
                _info.Children.Add(BuildProgress(readCount, _chapters.Count));

            if (_manga.Genres != null && _manga.Genres.Count > 0)
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 2, 0, 12) };
                foreach (var g in _manga.Genres)
                {
                    var genre = g;
                    chips.Children.Add(Ui.Chip(genre, () => _host.ShowCatalog(genre: genre, resetStack: false)));
                }
                _info.Children.Add(chips);
            }

            if (!string.IsNullOrWhiteSpace(_manga.Description))
            {
                _info.Children.Add(new TextBlock
                {
                    Text = _manga.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Theme.Muted,
                    FontSize = 13.5,
                    LineHeight = 21,
                    MaxWidth = 780,
                    Margin = new Thickness(0, 4, 0, 16),
                });
            }

            var actions = new WrapPanel();
            var history = LocalStore.Instance.GetHistory(_manga.Source, _manga.Id);
            var resume = history == null ? null : _chapters.FirstOrDefault(c => c.Id == history.ChapterId);
            var firstUnread = _chapters.FirstOrDefault(c => !LocalStore.Instance.IsRead(_manga.Source, c.Id));
            var target = resume ?? firstUnread ?? _chapters.FirstOrDefault();

            var label = target == null ? "Sem capítulos"
                : resume != null ? "▶  Continuar  " + ShortTitle(resume.Title)
                : readCount > 0 ? "▶  Continuar leitura"
                : "▶  Ler do início";
            _readBtn = Theme.Btn(label, "PrimaryButton");
            _readBtn.Margin = new Thickness(0, 0, 10, 8);
            _readBtn.MaxWidth = 300;
            _readBtn.IsEnabled = target != null;
            if (target != null) _readBtn.Click += (_, _) => OpenChapter(target);
            actions.Children.Add(_readBtn);

            if (_chapters.Count > 1 && (resume != null || readCount > 0 || firstUnread != null))
            {
                var fromStart = Theme.Btn("↺  Do início", "GhostButton");
                fromStart.Margin = new Thickness(0, 0, 10, 8);
                fromStart.Click += (_, _) => OpenChapter(_chapters[0]);
                actions.Children.Add(fromStart);

                if (_chapters.Count > 2)
                {
                    var latest = Theme.Btn("⏭  Último capítulo", "GhostButton");
                    latest.Margin = new Thickness(0, 0, 10, 8);
                    latest.Click += (_, _) => OpenChapter(_chapters[_chapters.Count - 1]);
                    actions.Children.Add(latest);
                }
            }

            var isFav = LocalStore.Instance.IsFavorite(_manga.Source, _manga.Id);
            _favBtn = Theme.Btn(isFav ? "♥  Nos favoritos" : "♡  Favoritar", "GhostButton");
            _favBtn.Margin = new Thickness(0, 0, 10, 8);
            _favBtn.Click += (_, _) =>
            {
                var nowFav = LocalStore.Instance.ToggleFavorite(_manga);
                _favBtn.Content = nowFav ? "♥  Nos favoritos" : "♡  Favoritar";
                _host.Toast(nowFav ? "Adicionado aos favoritos." : "Removido dos favoritos.");
            };
            actions.Children.Add(_favBtn);

            if (!string.IsNullOrWhiteSpace(_manga.Url))
            {
                var site = Theme.Btn("Abrir site ↗", "GhostButton");
                site.Margin = new Thickness(0, 0, 10, 8);
                site.Click += (_, _) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_manga.Url) { UseShellExecute = true }); }
                    catch (Exception ex) { _host.Toast(ex.Message, true); }
                };
                actions.Children.Add(site);

                var copy = Theme.Btn("Copiar link", "GhostButton");
                copy.Margin = new Thickness(0, 0, 10, 8);
                copy.Click += (_, _) =>
                {
                    try
                    {
                        Clipboard.SetText(_manga.Url);
                        _host.Toast("Link copiado.");
                    }
                    catch (Exception ex) { _host.Toast(ex.Message, true); }
                };
                actions.Children.Add(copy);
            }

            _info.Children.Add(actions);

            if (!string.IsNullOrEmpty(_manga.Cover)) LoadCover();
        }

        private FrameworkElement BuildProgress(int readCount, int total)
        {
            var pct = total <= 0 ? 0 : Math.Max(0, Math.Min(1, (double)readCount / total));
            var wrap = new StackPanel { Margin = new Thickness(0, 2, 0, 14), HorizontalAlignment = HorizontalAlignment.Left };
            var head = new DockPanel { Width = 360, Margin = new Thickness(0, 0, 0, 5) };
            head.Children.Add(new TextBlock
            {
                Text = "PROGRESSO DE LEITURA",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
            });
            var pctText = new TextBlock
            {
                Text = (int)Math.Round(pct * 100) + "%",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Accent2,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            DockPanel.SetDock(pctText, Dock.Right);
            head.Children.Add(pctText);
            wrap.Children.Add(head);

            var track = new Border
            {
                Width = 360,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = Theme.Panel2,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var fillGrid = new Grid();
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - pct, GridUnitType.Star) });
            var fill = new Border { Background = Theme.Grad, CornerRadius = new CornerRadius(3) };
            Grid.SetColumn(fill, 0);
            fillGrid.Children.Add(fill);
            track.Child = fillGrid;
            wrap.Children.Add(track);
            return wrap;
        }

        private static string ShortTitle(string title)
        {
            title = (title ?? string.Empty).Trim();
            if (title.Length <= 24) return title;
            return title.Substring(0, 23).TrimEnd() + "…";
        }

        private void LoadCover()
        {
            var cached = ImageLoader.GetCached(_manga.Cover, 520);
            if (cached != null)
            {
                _cover.Source = cached;
                _cover.Opacity = 1;
                return;
            }
            _ = LoadCoverAsync();
        }

        private async Task LoadCoverAsync()
        {
            var bmp = await ImageLoader.LoadAsync(_manga.Cover, 520, MangaCard.RefererFor(_manga.Source)).ConfigureAwait(true);
            if (bmp == null || _cover == null) return;
            _cover.Source = bmp;
            _cover.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }

        private void RenderChapters()
        {
            if (_manga == null) return;
            var gen = ++_renderGen;
            _chapterPanel.Children.Clear();
            _wrap = null;
            _showMore = null;

            var filtered = _chapters.AsEnumerable();
            var f = (_filter.Text ?? string.Empty).Trim();
            if (f.Length > 0)
                filtered = filtered.Where(c => c.Title.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
            var list = filtered.ToList();
            if (_descOrder) list.Reverse();

            _chapterCount.Text = f.Length > 0
                ? list.Count + " de " + _chapters.Count
                : _chapters.Count + " no total";

            _renderList = list;
            _renderShown = 0;

            if (list.Count == 0)
            {
                _chapterPanel.Children.Add(Ui.Empty("Nenhum capítulo encontrado."));
                return;
            }

            _wrap = new WrapPanel();
            _chapterPanel.Children.Add(_wrap);

            _showMore = Theme.Btn("Mostrar mais", "GhostButton");
            _showMore.HorizontalAlignment = HorizontalAlignment.Center;
            _showMore.Margin = new Thickness(0, 14, 0, 0);
            _showMore.Click += (_, _) => AddMoreChapters();
            _chapterPanel.Children.Add(_showMore);

            AddMoreChapters();
            if (gen != _renderGen) return;
        }

        /// <summary>
        /// Renderiza os capítulos em lotes. Obras com 1000+ capítulos abrem na hora:
        /// os primeiros aparecem e o restante fica sob o botão "Mostrar mais".
        /// </summary>
        private void AddMoreChapters()
        {
            if (_wrap == null || _manga == null) return;
            const int chunk = 120;
            var gen = _renderGen;
            var end = Math.Min(_renderList.Count, _renderShown + chunk);
            for (var i = _renderShown; i < end; i++)
            {
                if (gen != _renderGen) return;
                _wrap.Children.Add(BuildChapterButton(_renderList[i]));
            }
            _renderShown = end;

            if (_showMore == null) return;
            var remaining = _renderList.Count - _renderShown;
            if (remaining <= 0)
            {
                _showMore.Visibility = Visibility.Collapsed;
                return;
            }
            _showMore.Visibility = Visibility.Visible;
            _showMore.Content = "Mostrar mais " + Math.Min(chunk, remaining) + "  (" + remaining + " restantes)";
        }

        private Button BuildChapterButton(Chapter chapter)
        {
            var b = new Button
            {
                Style = (Style)Application.Current.Resources["ChipButton"],
                MinWidth = 156,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = string.IsNullOrWhiteSpace(chapter.Date) ? null : "Lançado em " + chapter.Date,
            };
            b.Click += (_, _) => OpenChapter(chapter);
            ApplyChapterStyle(b, chapter);
            b.MouseEnter += (_, _) =>
            {
                b.BorderBrush = Theme.AccentLine;
                b.Background = Theme.AccentSoft;
                b.Foreground = Theme.Accent2;
            };
            b.MouseLeave += (_, _) => ApplyChapterStyle(b, chapter);
            return b;
        }

        private void ApplyChapterStyle(Button b, Chapter chapter)
        {
            var read = LocalStore.Instance.IsRead(_manga.Source, chapter.Id);
            var current = !string.IsNullOrEmpty(_resumeChapterId) && chapter.Id == _resumeChapterId;
            b.Content = (read ? "✓  " : "") + chapter.Title;
            b.Background = current ? Theme.AccentSoft : (read ? Theme.Panel : Theme.Panel2);
            b.Foreground = current ? Theme.Accent2 : (read ? Theme.Muted : Theme.Text);
            b.BorderBrush = current ? Theme.AccentLine : Theme.Line;
            b.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
        }

        private void OpenChapter(Chapter chapter)
        {
            if (_manga == null) return;
            _host.ShowReader(_manga, _chapters, chapter);
        }
    }
}
