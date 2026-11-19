using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SoloMangasNative
{
    /// <summary>
    /// Busca do catálogo. Pode consultar apenas a fonte ativa ou todas as fontes
    /// ao mesmo tempo.
    /// 2.7:
    /// - cabeçalho fixo com a busca sempre visível;
    /// - debounce de 240 ms e cache de 2,5 min por termo/filtro;
    /// - busca global progressiva: cada fonte entrega seus resultados na hora,
    ///   sem esperar as fontes lentas (timeout individual de 12 s);
    /// - esqueletos e carregamento de capas só no viewport.
    /// </summary>
    public sealed class CatalogPage : UserControl, IRefreshable, IPageSource, IAdultAware
    {
        private readonly MainWindow _host;
        private readonly ISource _source;
        private readonly SearchField _search;
        private readonly Button _genreBtn;
        private readonly Button _adultBtn;
        private readonly Button _scopeBtn;
        private readonly Button _clearFilters;
        private readonly TextBlock _count;
        private readonly TextBlock _status;
        private readonly TextBlock _subtitle;
        private readonly TextBlock _scopeHint;
        private readonly Border _scopeChip;
        private readonly MangaGrid _grid;
        private readonly Button _more;
        private readonly ScrollViewer _scroll;
        private readonly DispatcherTimer _debounce;
        private readonly StackPanel _recentPanel = new StackPanel();

        private string _genre;
        private int _page = 1;
        private bool _hasMore;
        private bool _loadingMore;
        private bool _genresLoaded;
        private int _gen;              // descarta respostas de buscas antigas
        private string _lastQuery;     // evita refazer a mesma busca
        private string _lastQ = string.Empty;
        private bool _global;
        private int _shown;

        // Busca global progressiva
        private readonly List<Manga> _stream = new List<Manga>();
        private readonly HashSet<string> _streamKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<SourceHit> _streamHits = new List<SourceHit>();
        private int _streamDone;
        private bool _streamFirstAppend;

        // Cache de resultados
        private sealed class GlobalCache
        {
            public DateTime At;
            public List<SourceHit> Hits;
            public int SourcesTotal;
        }

        private sealed class SingleCache
        {
            public DateTime At;
            public CatalogResult Result;
        }

        private readonly Dictionary<string, GlobalCache> _globalCache =
            new Dictionary<string, GlobalCache>(StringComparer.Ordinal);
        private readonly Dictionary<string, SingleCache> _singleCache =
            new Dictionary<string, SingleCache>(StringComparer.Ordinal);

        public string PageSource => _source.Key;

        public CatalogPage(MainWindow host, string q = null, string genre = null)
        {
            _host = host;
            _source = host.Current;
            _genre = genre ?? string.Empty;
            _global = LocalStore.Instance.Settings.GlobalSearch;

            // ---------------- Cabeçalho fixo ----------------
            var header = new Border
            {
                Background = Theme.Header,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(34, 14, 34, 14),
            };
            var headStack = new StackPanel();

            var titleRow = new DockPanel { LastChildFill = false };
            var titles = new StackPanel();
            titles.Children.Add(Theme.H1("Buscar"));
            _subtitle = new TextBlock
            {
                Text = "Encontre obras pelo título, gênero ou fonte",
                FontSize = 12.5,
                Foreground = Theme.Muted,
                Margin = new Thickness(2, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 420,
            };
            titles.Children.Add(_subtitle);
            DockPanel.SetDock(titles, Dock.Left);
            titleRow.Children.Add(titles);

            _scopeHint = new TextBlock
            {
                Text = _global ? "Todas as fontes" : _source.Name,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = _global ? Theme.Accent2 : Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _scopeChip = new Border
            {
                Background = _global ? Theme.AccentSoft : Theme.Panel2,
                BorderBrush = _global ? Theme.AccentLine : Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(11, 5, 11, 5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Child = _scopeHint,
            };
            DockPanel.SetDock(_scopeChip, Dock.Right);
            titleRow.Children.Add(_scopeChip);
            headStack.Children.Add(titleRow);

            var searchRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _search = new SearchField("Busque por título, autor ou gênero…", double.NaN);
            _search.HorizontalAlignment = HorizontalAlignment.Stretch;
            _search.Margin = new Thickness(0, 0, 10, 0);
            _search.Text = q ?? string.Empty;
            searchRow.Children.Add(_search);

            var searchBtn = Theme.Btn("Buscar", "PrimaryButton");
            searchBtn.MinWidth = 96;
            searchBtn.Height = 40;
            searchBtn.VerticalAlignment = VerticalAlignment.Center;
            searchBtn.Click += (_, _) =>
            {
                _debounce.Stop();
                _ = RunUserSearchAsync();
            };
            Grid.SetColumn(searchBtn, 1);
            searchRow.Children.Add(searchBtn);
            headStack.Children.Add(searchRow);
            header.Child = headStack;

            // ---------------- Conteúdo rolável ----------------
            var root = new StackPanel { Margin = new Thickness(34, 16, 34, 48) };

            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            _scopeBtn = Theme.Btn(ScopeLabel(), _global ? "SoftButton" : "GhostButton");
            _scopeBtn.Margin = new Thickness(0, 0, 8, 8);
            _scopeBtn.ToolTip = "Alternar entre buscar só nesta fonte ou em todas as fontes";
            _scopeBtn.Click += (_, _) =>
            {
                _global = !_global;
                LocalStore.Instance.Settings.GlobalSearch = _global;
                LocalStore.Instance.SaveSoon();
                _scopeBtn.Content = ScopeLabel();
                _scopeBtn.Style = (Style)Application.Current.Resources[_global ? "SoftButton" : "GhostButton"];
                _grid.ShowSource = _global;
                UpdateScopeChip();
                RenderRecentSearches();
                _ = RunSearchAsync(force: true);
            };
            bar.Children.Add(_scopeBtn);

            _genreBtn = Theme.Btn(GenreLabel(), "GhostButton");
            _genreBtn.Margin = new Thickness(0, 0, 8, 8);
            _genreBtn.Click += (_, _) => OpenGenreMenu();
            bar.Children.Add(_genreBtn);

            _adultBtn = Theme.Btn(AdultLabel(), "GhostButton");
            _adultBtn.Margin = new Thickness(0, 0, 8, 8);
            _adultBtn.Click += (_, _) => _host.ToggleAdult();
            bar.Children.Add(_adultBtn);

            _clearFilters = Theme.Btn("Limpar filtros", "LinkButton");
            _clearFilters.Margin = new Thickness(0, 0, 10, 8);
            _clearFilters.VerticalAlignment = VerticalAlignment.Center;
            _clearFilters.Visibility = Visibility.Collapsed;
            _clearFilters.Click += (_, _) =>
            {
                _search.Text = string.Empty;
                _genre = string.Empty;
                _genreBtn.Content = GenreLabel();
                UpdateClearFilters();
                _debounce.Stop();
                _ = RunSearchAsync(force: true);
            };
            bar.Children.Add(_clearFilters);
            root.Children.Add(bar);

            root.Children.Add(_recentPanel);

            _count = new TextBlock { Foreground = Theme.Muted, FontSize = 12.5, Margin = new Thickness(2, 4, 0, 10) };
            root.Children.Add(_count);

            _status = new TextBlock
            {
                Foreground = Theme.Muted,
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 12),
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap,
            };
            root.Children.Add(_status);

            _grid = new MangaGrid { ShowSource = _global };
            root.Children.Add(_grid);

            _more = Theme.Btn("Carregar mais", "GhostButton");
            _more.HorizontalAlignment = HorizontalAlignment.Center;
            _more.Margin = new Thickness(0, 14, 0, 0);
            _more.Visibility = Visibility.Collapsed;
            _more.Click += async (_, _) => await LoadMoreAsync();
            root.Children.Add(_more);

            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.Children.Add(header);
            _scroll = Ui.Scroll(root);
            Grid.SetRow(_scroll, 1);
            shell.Children.Add(_scroll);
            Content = shell;
            _scroll.ScrollChanged += OnScrollChanged;

            _search.Box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    _debounce.Stop();
                    _ = RunUserSearchAsync();
                }
            };

            // Busca automática enquanto digita: 240 ms de pausa.
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                _ = RunSearchAsync();
            };
            _search.Box.TextChanged += (_, _) =>
            {
                UpdateClearFilters();
                _debounce.Stop();
                var text = (_search.Text ?? string.Empty).Trim();
                if (text.Length == 1)
                {
                    ShowMinimumHint();
                    return;
                }
                _debounce.Start();
            };

            UpdateClearFilters();
            RenderRecentSearches();
        }

        private string ScopeKey => _global ? "__global" : _source.Key;

        private string ScopeLabel() =>
            _global ? "Busca: todas as fontes" : "Busca: " + _source.Name;

        private string GenreLabel() => "Gênero: " + (string.IsNullOrEmpty(_genre) ? "Todos" : _genre);

        private string AdultLabel() => LocalStore.Instance.Settings.Adult ? "＋18: ativado" : "＋18: desativado";

        private void UpdateScopeChip()
        {
            _scopeHint.Text = _global ? "Todas as fontes" : _source.Name;
            _scopeHint.Foreground = _global ? Theme.Accent2 : Theme.Muted;
            _scopeChip.Background = _global ? Theme.AccentSoft : Theme.Panel2;
            _scopeChip.BorderBrush = _global ? Theme.AccentLine : Theme.Line;
        }

        /// <summary>Chamado pelo botão global +18: atualiza o rótulo e refaz a busca.</summary>
        public void AdultChanged()
        {
            _adultBtn.Content = AdultLabel();
            _ = RunSearchAsync(force: true);
        }

        private void UpdateClearFilters()
        {
            var active = !string.IsNullOrWhiteSpace((_search.Text ?? string.Empty).Trim()) || !string.IsNullOrEmpty(_genre);
            _clearFilters.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RenderRecentSearches()
        {
            _recentPanel.Children.Clear();
            var recents = LocalStore.Instance.RecentSearches(ScopeKey, 8);
            if (recents.Count == 0)
            {
                _recentPanel.Visibility = Visibility.Collapsed;
                return;
            }

            _recentPanel.Visibility = Visibility.Visible;
            _recentPanel.Margin = new Thickness(0, 2, 0, 8);

            var head = new DockPanel { LastChildFill = false };
            head.Children.Add(new TextBlock
            {
                Text = _global ? "BUSCAS RECENTES · TODAS AS FONTES" : "BUSCAS RECENTES",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var clear = Theme.Btn("limpar", "LinkButton");
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            clear.Click += (_, _) =>
            {
                LocalStore.Instance.ClearSearches(ScopeKey);
                RenderRecentSearches();
            };
            DockPanel.SetDock(clear, Dock.Right);
            head.Children.Add(clear);
            _recentPanel.Children.Add(head);

            var wrap = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            foreach (var entry in recents)
            {
                var q = entry.Query;
                wrap.Children.Add(Ui.Chip(q, () =>
                {
                    _search.Text = q;
                    _debounce.Stop();
                    _ = RunUserSearchAsync();
                }));
            }
            _recentPanel.Children.Add(wrap);
        }

        /// <summary>Busca disparada pelo usuário: guarda o termo no histórico local.</summary>
        private async Task RunUserSearchAsync()
        {
            var q = (_search.Text ?? string.Empty).Trim();
            if (q.Length >= 2)
            {
                LocalStore.Instance.AddSearch(ScopeKey, q);
                RenderRecentSearches();
            }
            await RunSearchAsync(force: true);
        }

        public void FocusSearch()
        {
            _search.Box.Focus();
            _search.Box.CaretIndex = (_search.Text ?? string.Empty).Length;
        }

        // Modo infinito: chegando perto do fim do resultado, busca a próxima página.
        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!IsLoaded || !IsVisible) return;
            if (_global || !_hasMore || _loadingMore) return;
            if (e.ExtentHeight <= 0 || e.ViewportHeight <= 1) return;
            var remaining = e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight);
            if (remaining <= 900) _ = LoadMoreAsync();
        }

        private void OpenGenreMenu()
        {
            var menu = new ContextMenu
            {
                Background = Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                MaxHeight = 480,
            };

            void AddItem(string label)
            {
                var mi = new MenuItem
                {
                    Header = label,
                    Foreground = Theme.Text,
                    Background = Theme.Panel,
                    Padding = new Thickness(12, 6, 24, 6),
                };
                mi.Click += (_, _) =>
                {
                    _genre = label == "Todos" ? string.Empty : label;
                    _genreBtn.Content = GenreLabel();
                    UpdateClearFilters();
                    _ = RunSearchAsync(force: true);
                };
                menu.Items.Add(mi);
            }

            AddItem("Todos");
            if (_genresLoaded)
            {
                foreach (var g in _genres) AddItem(g);
            }
            else
            {
                var loading = new MenuItem { Header = "Carregando…", IsEnabled = false };
                menu.Items.Add(loading);
                _ = LoadGenresAsync(menu, loading);
            }
            menu.PlacementTarget = _genreBtn;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private List<string> _genres = new List<string>();

        private async Task LoadGenresAsync(ContextMenu menu, MenuItem loading)
        {
            try
            {
                List<string> genres;
                if (_global)
                {
                    var all = await Task.WhenAll(SourceRegistry.Instance.Enabled(LocalStore.Instance.Settings.Adult).Select(async s =>
                    {
                        try { return await s.GenresAsync().ConfigureAwait(false); }
                        catch { return new List<string>(); }
                    }));
                    genres = all.SelectMany(g => g)
                        .Where(g => !string.IsNullOrWhiteSpace(g))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(g => g, StringComparer.CurrentCulture)
                        .ToList();
                }
                else
                {
                    genres = await _source.GenresAsync().ConfigureAwait(true);
                }

                _genres = genres;
                _genresLoaded = true;
                menu.Items.Remove(loading);
                foreach (var g in genres)
                {
                    var label = g;
                    var mi = new MenuItem
                    {
                        Header = label,
                        Foreground = Theme.Text,
                        Background = Theme.Panel,
                        Padding = new Thickness(12, 6, 24, 6),
                    };
                    mi.Click += (_, _) =>
                    {
                        _genre = label;
                        _genreBtn.Content = GenreLabel();
                        UpdateClearFilters();
                        _ = RunSearchAsync(force: true);
                    };
                    menu.Items.Add(mi);
                }
            }
            catch (Exception ex)
            {
                loading.Header = "Falha: " + ex.Message;
            }
        }

        public Task LoadAsync() => RunSearchAsync(force: false);

        /// <summary>Recarrega ao voltar para a página, sem apagar o que já está na tela.</summary>
        public void Refresh() => _ = RunSearchAsync(force: false);

        private sealed class SourceHit
        {
            public ISource Source;
            public int Index;
            public CatalogResult Result;
            public string Error;
        }

        private static async Task<SourceHit> QueryAsync(ISource source, int index, SourceQuery query)
        {
            try
            {
                return new SourceHit
                {
                    Source = source,
                    Index = index,
                    Result = await source.CatalogAsync(query).ConfigureAwait(false),
                };
            }
            catch (Exception ex)
            {
                return new SourceHit { Source = source, Index = index, Error = ex.Message };
            }
        }

        private static async Task<SourceHit> QueryWithTimeoutAsync(ISource source, int index, SourceQuery query, int timeoutMs)
        {
            var task = QueryAsync(source, index, query);
            var done = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (done == task) return await task.ConfigureAwait(false);
            return new SourceHit { Source = source, Index = index, Error = "tempo esgotado" };
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int timeoutMs)
        {
            var done = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (done == task) return await task.ConfigureAwait(false);
            throw new TimeoutException("A fonte demorou demais para responder.");
        }

        private string QueryKey(string q) =>
            (_global ? "g" : "s:" + _source.Key) + "\u0001" +
            (q ?? string.Empty).Trim().ToLowerInvariant() + "\u0001" +
            (_genre ?? string.Empty).ToLowerInvariant() + "\u0001" +
            (LocalStore.Instance.Settings.Adult ? "1" : "0");

        private static bool Fresh(DateTime at) => (DateTime.UtcNow - at).TotalSeconds < 150;

        private void CacheSingle(string key, CatalogResult result)
        {
            if (_singleCache.Count >= 16)
            {
                var oldest = _singleCache.OrderBy(kv => kv.Value.At).First();
                _singleCache.Remove(oldest.Key);
            }
            _singleCache[key] = new SingleCache { At = DateTime.UtcNow, Result = result };
        }

        private void CacheGlobal(string key, List<SourceHit> hits, int sourcesTotal)
        {
            if (_globalCache.Count >= 10)
            {
                var oldest = _globalCache.OrderBy(kv => kv.Value.At).First();
                _globalCache.Remove(oldest.Key);
            }
            _globalCache[key] = new GlobalCache
            {
                At = DateTime.UtcNow,
                Hits = hits.ToList(),
                SourcesTotal = sourcesTotal,
            };
        }

        private void ShowMinimumHint()
        {
            _gen++;
            _grid.ClearItems();
            _hasMore = false;
            _loadingMore = false;
            _more.Visibility = Visibility.Collapsed;
            _count.Text = string.Empty;
            _status.Visibility = Visibility.Visible;
            _status.Foreground = Theme.Muted;
            _status.Text = "Digite ao menos 2 caracteres para buscar.";
        }

        private async Task RunSearchAsync(bool force = false)
        {
            var q = (_search.Text ?? string.Empty).Trim();
            var key = QueryKey(q);
            if (!force && key == _lastQuery && _grid.Children.Count > 0) return;

            // Cache de 2,5 min: voltar a um termo recente é instantâneo.
            if (!force)
            {
                if (_global && _globalCache.TryGetValue(key, out var gc) && Fresh(gc.At))
                {
                    _lastQuery = key;
                    _lastQ = q;
                    ApplyGlobal(gc.Hits, q, gc.SourcesTotal, streaming: false);
                    return;
                }
                if (!_global && _singleCache.TryGetValue(key, out var sc) && Fresh(sc.At))
                {
                    _lastQuery = key;
                    _lastQ = q;
                    ApplySingle(sc.Result);
                    return;
                }
            }

            _lastQuery = key;
            _lastQ = q;

            var gen = ++_gen;
            _page = 1;
            _hasMore = false;
            _loadingMore = false;
            _shown = 0;

            _grid.ShowSource = _global;
            _grid.ShowSkeleton(12);
            _scroll.ScrollToTop();
            _more.Visibility = Visibility.Collapsed;
            _more.IsEnabled = true;
            _more.Content = "Carregar mais";
            _status.Visibility = Visibility.Visible;
            _status.Foreground = Theme.Muted;
            _status.Text = _global ? "Consultando todas as fontes…" : "Carregando…";
            _count.Text = string.Empty;

            try
            {
                if (_global) await RunGlobalAsync(q, gen);
                else await RunSingleAsync(q, gen);
            }
            finally
            {
                if (gen == _gen)
                {
                    UpdateClearFilters();
                    AutoFill();
                }
            }
        }

        private async Task RunSingleAsync(string q, int gen)
        {
            try
            {
                var result = await WithTimeout(_source.CatalogAsync(new SourceQuery
                {
                    Page = 1,
                    Limit = 32,
                    Q = q,
                    Genre = _genre,
                    Adult = LocalStore.Instance.Settings.Adult,
                }), 16000);

                if (gen != _gen) return;
                ApplySingle(result);
                CacheSingle(QueryKey(q), result);
            }
            catch (Exception ex)
            {
                if (gen != _gen) return;
                _status.Visibility = Visibility.Visible;
                _status.Foreground = Theme.Accent2;
                _status.Text = "Falha na busca: " + ex.Message;
            }
        }

        private void ApplySingle(CatalogResult result)
        {
            _status.Foreground = Theme.Muted;
            _grid.ShowSource = false;
            _grid.SetItems(result.Items, mm => _host.ShowManga(mm));
            _shown = result.Items.Count;
            _status.Visibility = result.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (result.Items.Count == 0) _status.Text = "Nenhum resultado encontrado.";
            _count.Text = result.Total > 0 ? result.Total + " obras encontradas" : result.Items.Count + " obras";
            _hasMore = result.HasMore && result.Items.Count > 0;
            _more.Visibility = _hasMore ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Consulta todas as fontes em paralelo e mostra cada fonte assim que ela
        /// responde. Fontes lentas têm 12 s e não seguram o restante do resultado.
        /// </summary>
        private async Task RunGlobalAsync(string q, int gen)
        {
            var sources = SourceRegistry.Instance.Enabled(LocalStore.Instance.Settings.Adult).ToList();
            var query = new SourceQuery
            {
                Page = 1,
                Limit = q.Length == 0 ? 24 : 36,
                Q = q,
                Genre = _genre,
                Adult = LocalStore.Instance.Settings.Adult,
            };

            var pending = new List<Task<SourceHit>>();
            for (var i = 0; i < sources.Count; i++)
                pending.Add(QueryWithTimeoutAsync(sources[i], i, query, 12000));

            _stream.Clear();
            _streamKeys.Clear();
            _streamHits.Clear();
            _streamDone = 0;
            _streamFirstAppend = true;
            _status.Visibility = Visibility.Collapsed;
            _count.Text = "Consultando " + sources.Count + " fontes…";

            while (pending.Count > 0)
            {
                var done = await Task.WhenAny(pending);
                pending.Remove(done);
                var hit = await done;
                if (gen != _gen) return;

                _streamHits.Add(hit);
                _streamDone++;

                if (hit.Error == null && hit.Result?.Items != null)
                {
                    var added = new List<Manga>();
                    foreach (var m in hit.Result.Items)
                    {
                        if (m == null || string.IsNullOrWhiteSpace(m.Title)) continue;
                        if (string.IsNullOrWhiteSpace(m.Source)) m.Source = hit.Source.Key;
                        var dk = DedupeKey(m);
                        if (!_streamKeys.Add(dk)) continue;
                        _stream.Add(m);
                        added.Add(m);
                    }

                    if (added.Count > 0)
                    {
                        if (_streamFirstAppend)
                        {
                            // Remove os esqueletos e fixa o callback dos cards.
                            _streamFirstAppend = false;
                            _grid.SetItems(Array.Empty<Manga>(), mm => _host.ShowManga(mm));
                        }
                        if (_stream.Count <= 140) _grid.Append(added);
                    }
                }

                UpdateStreamStatus(sources.Count, pending.Count);
            }

            if (gen != _gen) return;
            ApplyGlobal(_streamHits, q, sources.Count, streaming: false);
            CacheGlobal(QueryKey(q), _streamHits, sources.Count);
        }

        private void UpdateStreamStatus(int sourcesTotal, int pending)
        {
            var errors = _streamHits.Count(h => h.Error != null);
            var text = _stream.Count + " obra(s) · " + _streamDone + " de " + sourcesTotal + " fontes";
            if (pending > 0) text += " · carregando…";
            if (errors > 0) text += " · " + errors + " sem resposta";
            _count.Text = text;
        }

        private void ApplyGlobal(List<SourceHit> hits, string q, int sourcesTotal, bool streaming)
        {
            var ok = hits.Where(h => h.Error == null && h.Result != null).ToList();
            var failed = hits.Where(h => h.Error != null).ToList();
            var merged = q.Length == 0 ? Interleave(ok) : Rank(ok, q);
            if (streaming && merged.Count > 120) merged = merged.Take(120).ToList();

            _grid.ShowSource = true;
            _grid.SetItems(merged, mm => _host.ShowManga(mm));
            _hasMore = false;
            _more.Visibility = Visibility.Collapsed;

            var count = merged.Count + " obra(s) · " + ok.Count + " de " + sourcesTotal + " fontes";
            if (failed.Count > 0) count += " · " + failed.Count + " sem resposta";
            _count.Text = count;

            if (merged.Count == 0)
            {
                _status.Visibility = Visibility.Visible;
                _status.Foreground = failed.Count == sourcesTotal ? Theme.Accent2 : Theme.Muted;
                _status.Text = failed.Count == sourcesTotal
                    ? "Todas as fontes falharam agora. Tente de novo em instantes."
                    : "Nenhuma obra encontrada em nenhuma fonte.";
            }
            else if (failed.Count > 0)
            {
                _status.Visibility = Visibility.Visible;
                _status.Foreground = Theme.Muted;
                _status.Text = "Sem resposta agora: " + string.Join(", ", failed.Select(f => f.Source.Name)) + ".";
            }
            else
            {
                _status.Visibility = Visibility.Collapsed;
            }

            Log.Write("catalog: global q='" + q + "' -> " + merged.Count + " de " + ok.Count + "/" + sourcesTotal + " fontes");
        }

        /// <summary>Mistura as fontes em rodízio para a tela inicial da busca global.</summary>
        private static List<Manga> Interleave(List<SourceHit> hits)
        {
            var buckets = hits.OrderBy(h => h.Index)
                .Select(h => (Source: h.Source, Items: h.Result.Items ?? new List<Manga>()))
                .ToList();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var outp = new List<Manga>();
            var max = buckets.Count == 0 ? 0 : buckets.Max(b => b.Items.Count);
            for (var i = 0; i < max; i++)
            {
                foreach (var bucket in buckets)
                {
                    if (i >= bucket.Items.Count) continue;
                    var m = bucket.Items[i];
                    if (m == null || string.IsNullOrWhiteSpace(m.Title)) continue;
                    if (string.IsNullOrWhiteSpace(m.Source)) m.Source = bucket.Source.Key;
                    var key = DedupeKey(m);
                    if (!seen.Add(key)) continue;
                    outp.Add(m);
                }
            }
            return outp;
        }

        /// <summary>Ordena por relevância do título e agrupa obras repetidas entre fontes.</summary>
        private static List<Manga> Rank(List<SourceHit> hits, string q)
        {
            var best = new Dictionary<string, (Manga Manga, int Score, int SourceIdx)>(StringComparer.Ordinal);
            foreach (var hit in hits.OrderBy(h => h.Index))
            {
                foreach (var m in hit.Result.Items ?? new List<Manga>())
                {
                    if (m == null || string.IsNullOrWhiteSpace(m.Title)) continue;
                    if (string.IsNullOrWhiteSpace(m.Source)) m.Source = hit.Source.Key;
                    var key = DedupeKey(m);
                    var score = TitleTools.Score(m.Title, q);
                    if (!best.TryGetValue(key, out var cur))
                    {
                        best[key] = (m, score, hit.Index);
                        continue;
                    }
                    if (score < cur.Score ||
                        (score == cur.Score && m.Chapters > cur.Manga.Chapters))
                    {
                        best[key] = (m, score, hit.Index);
                    }
                }
            }

            var ranked = new List<Manga>();
            foreach (var group in best.Values.GroupBy(v => v.Score).OrderBy(g => g.Key))
            {
                var buckets = group
                    .GroupBy(v => v.SourceIdx)
                    .OrderBy(g => g.Key)
                    .Select(g => g.OrderBy(v => v.Manga.Title, StringComparer.CurrentCultureIgnoreCase).ToList())
                    .ToList();
                var max = buckets.Count == 0 ? 0 : buckets.Max(b => b.Count);
                for (var i = 0; i < max; i++)
                    foreach (var bucket in buckets)
                        if (i < bucket.Count)
                            ranked.Add(bucket[i].Manga);
            }
            return ranked;
        }

        private static string DedupeKey(Manga m)
        {
            var key = TitleTools.Fold(m.Title);
            return key.Length == 0 ? m.Key : key;
        }

        // Se a primeira página não preencheu a tela, já busca a próxima.
        private void AutoFill()
        {
            if (_global) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded || !IsVisible) return;
                if (!_hasMore || _loadingMore) return;
                if (_scroll.ViewportHeight <= 1) return;
                if (_scroll.ExtentHeight <= _scroll.ViewportHeight + 4) _ = LoadMoreAsync();
            }));
        }

        private async Task LoadMoreAsync()
        {
            if (_loadingMore || !_hasMore || _global) return;
            _loadingMore = true;
            var gen = _gen;
            var nextPage = _page + 1;
            _more.IsEnabled = false;
            _more.Content = "Carregando…";
            try
            {
                var result = await _source.CatalogAsync(new SourceQuery
                {
                    Page = nextPage,
                    Limit = 32,
                    Q = _lastQ,
                    Genre = _genre,
                    Adult = LocalStore.Instance.Settings.Adult,
                });
                if (gen != _gen) return;
                _page = nextPage;
                _grid.Append(result.Items);
                _shown += result.Items.Count;
                if (_shown > 0) _count.Text = _shown + " obras carregadas";
                Log.Write("catalog: pagina " + _page + " +" + result.Items.Count + " hasMore=" + result.HasMore);
                _hasMore = result.HasMore && result.Items.Count > 0;
                _more.Visibility = _hasMore ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (gen != _gen) return;
                _hasMore = false;
                _host.Toast(ex.Message, true);
            }
            finally
            {
                if (gen == _gen)
                {
                    _loadingMore = false;
                    _more.IsEnabled = true;
                    _more.Content = "Carregar mais";
                    AutoFill();
                }
            }
        }
    }
}
