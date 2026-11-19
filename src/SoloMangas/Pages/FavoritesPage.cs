using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoloMangasNative
{
    /// <summary>Favoritos salvos no computador, com retomada de leitura por obra.</summary>
    public sealed class FavoritesPage : UserControl, IRefreshable
    {
        private readonly MainWindow _host;
        private readonly StackPanel _list;
        private readonly TextBlock _subtitle;
        private readonly TextBlock _stats;
        private bool _recentFirst = true;

        public FavoritesPage(MainWindow host)
        {
            _host = host;
            var root = new StackPanel { Margin = new Thickness(34, 26, 34, 46) };

            var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 4) };
            var titles = new StackPanel();
            titles.Children.Add(Theme.H1("Favoritos"));
            _subtitle = new TextBlock
            {
                Text = "Suas obras salvas, de todas as fontes",
                FontSize = 13,
                Foreground = Theme.Muted,
                Margin = new Thickness(2, 2, 0, 0),
            };
            titles.Children.Add(_subtitle);
            DockPanel.SetDock(titles, Dock.Left);
            head.Children.Add(titles);

            var sortBtn = Theme.Btn("Ordem: recentes", "GhostButton");
            sortBtn.VerticalAlignment = VerticalAlignment.Center;
            sortBtn.ToolTip = "Alternar entre os favoritados mais recentes e a ordem alfabética";
            sortBtn.Click += (_, _) =>
            {
                _recentFirst = !_recentFirst;
                sortBtn.Content = _recentFirst ? "Ordem: recentes" : "Ordem: A–Z";
                Render();
            };
            DockPanel.SetDock(sortBtn, Dock.Right);
            head.Children.Add(sortBtn);
            root.Children.Add(head);

            root.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 16, 0, 0),
                Background = new LinearGradientBrush(Theme.AccentColor, Colors.Transparent, new Point(0, 0), new Point(1, 0)),
            });

            _stats = new TextBlock
            {
                FontSize = 12.5,
                Foreground = Theme.Muted,
                Margin = new Thickness(2, 16, 0, 10),
            };
            root.Children.Add(_stats);

            _list = new StackPanel();
            root.Children.Add(_list);

            Content = Ui.Scroll(root);
            Render();
        }

        public void Refresh() => Render();

        private void Render()
        {
            _list.Children.Clear();
            var favs = LocalStore.Instance.Favorites.ToList();
            if (!_recentFirst)
                favs = favs.OrderBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

            var withHistory = favs.Count(f => LocalStore.Instance.GetHistory(f.Source, f.Id) != null);
            var sources = favs.Select(f => f.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _stats.Text = favs.Count + " obra(s) · " + withHistory + " com leitura em andamento · " + sources + " fonte(s)";
            _subtitle.Text = favs.Count == 0
                ? "Suas obras salvas, de todas as fontes"
                : "Toque em uma obra para abrir; use Continuar para retomar de onde parou.";

            if (favs.Count == 0)
            {
                var box = new StackPanel { Margin = new Thickness(0, 30, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                box.Children.Add(new TextBlock
                {
                    Text = "Nenhuma obra favoritada ainda.",
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.Text,
                });
                box.Children.Add(new TextBlock
                {
                    Text = "Abra uma obra e toque em ♡ Favoritar para guardá-la aqui.",
                    FontSize = 13,
                    Foreground = Theme.Muted,
                    Margin = new Thickness(0, 6, 0, 14),
                });
                var browse = Theme.Btn("Explorar catálogo", "PrimaryButton");
                browse.HorizontalAlignment = HorizontalAlignment.Left;
                browse.Click += (_, _) => _host.ShowCatalog();
                box.Children.Add(browse);
                _list.Children.Add(box);
                return;
            }

            foreach (var f in favs)
            {
                var entry = f;
                _list.Children.Add(BuildRow(entry));
            }
        }

        private Border BuildRow(FavEntry entry)
        {
            var hist = LocalStore.Instance.GetHistory(entry.Source, entry.Id);
            var row = new Border
            {
                Background = Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand,
                ToolTip = "Abrir " + entry.Title,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var coverBox = new Border
            {
                Width = 58,
                Height = 82,
                CornerRadius = new CornerRadius(8),
                Background = Theme.Panel2,
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Ui.RoundClip(coverBox, 8);
            var coverGrid = new Grid();
            coverGrid.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.Title) ? "?" : entry.Title.Trim().Substring(0, 1).ToUpperInvariant(),
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = Theme.AccentSoft,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            });
            var img = new Image { Stretch = Stretch.UniformToFill };
            coverGrid.Children.Add(img);
            coverBox.Child = coverGrid;
            if (!string.IsNullOrEmpty(entry.Cover))
            {
                var cached = ImageLoader.GetCached(entry.Cover, 160);
                if (cached != null) img.Source = cached;
                else
                {
                    _ = ImageLoader.LoadAsync(entry.Cover, 160, MangaCard.RefererFor(entry.Source))
                        .ContinueWith(t =>
                        {
                            if (t.Result != null) Dispatcher.BeginInvoke(new Action(() => img.Source = t.Result));
                        });
                }
            }
            Grid.SetColumn(coverBox, 0);
            grid.Children.Add(coverBox);

            var texts = new StackPanel { Margin = new Thickness(13, 2, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = entry.Title,
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left,
            });

            var badges = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            badges.Children.Add(Badge(SourceRegistry.Instance.NameOf(entry.Source), Theme.Accent2, Theme.AccentSoft));
            if (entry.Adult) badges.Children.Add(Badge("+18", Theme.Accent2, Theme.DangerSoft));
            badges.Children.Add(new TextBlock
            {
                Text = "favoritado em " + entry.Added.ToString("dd/MM/yyyy"),
                FontSize = 11,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
            });
            texts.Children.Add(badges);

            if (hist != null && !string.IsNullOrWhiteSpace(hist.ChapterId))
            {
                var pct = (int)Math.Round(Math.Max(0, Math.Min(1, hist.Position)) * 100);
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                line.Children.Add(new TextBlock
                {
                    Text = hist.ChapterTitle,
                    FontSize = 12,
                    Foreground = Theme.Text,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 340,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                line.Children.Add(new TextBlock
                {
                    Text = "  ·  " + pct + "%  ·  " + hist.Updated.ToString("dd/MM HH:mm"),
                    FontSize = 11,
                    Foreground = Theme.Muted,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                texts.Children.Add(line);

                var track = new Border
                {
                    Height = 4,
                    Width = 240,
                    CornerRadius = new CornerRadius(2),
                    Background = Theme.Panel2,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                var fillGrid = new Grid();
                fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, Math.Min(1, hist.Position)), GridUnitType.Star) });
                fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - Math.Min(1, hist.Position)), GridUnitType.Star) });
                var fill = new Border { Background = Theme.Grad, CornerRadius = new CornerRadius(2) };
                Grid.SetColumn(fill, 0);
                fillGrid.Children.Add(fill);
                track.Child = fillGrid;
                texts.Children.Add(track);
            }
            else
            {
                texts.Children.Add(new TextBlock
                {
                    Text = "Ainda não lida",
                    FontSize = 11.5,
                    Foreground = Theme.Muted,
                    Margin = new Thickness(0, 7, 0, 0),
                });
            }

            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (hist != null && !string.IsNullOrEmpty(hist.ChapterId))
            {
                var cont = Theme.Btn("Continuar", "PrimaryButton");
                cont.Margin = new Thickness(0, 0, 8, 0);
                cont.ToolTip = hist.ChapterTitle;
                cont.Click += (_, _) => _host.ShowManga(entry.Source, entry.Id, entry.Adult, hist.ChapterId);
                actions.Children.Add(cont);
            }
            var open = Theme.Btn(hist != null ? "Do início" : "Abrir", "GhostButton");
            open.Margin = new Thickness(0, 0, 8, 0);
            open.Click += (_, _) => _host.ShowManga(entry.Source, entry.Id, entry.Adult);
            actions.Children.Add(open);

            var remove = Theme.Btn("✕", "IconButton");
            remove.ToolTip = "Remover dos favoritos";
            remove.Click += (_, _) =>
            {
                LocalStore.Instance.RemoveFavorite(entry.Source, entry.Id);
                _host.Toast("Removido dos favoritos.");
                Render();
            };
            actions.Children.Add(remove);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            row.Child = grid;
            row.MouseEnter += (_, _) => { row.Background = Theme.Panel2; row.BorderBrush = Theme.AccentLine; };
            row.MouseLeave += (_, _) => { row.Background = Theme.Panel; row.BorderBrush = Theme.Line; };
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && Ui.FindAncestor<Button>(d) != null) return;
                _host.ShowManga(entry.Source, entry.Id, entry.Adult);
            };
            return row;
        }

        private static Border Badge(string text, Brush fg, Brush bg) => new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
            },
        };
    }
}
