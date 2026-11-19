using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoloMangasNative
{
    /// <summary>Histórico de leitura agrupado por recência, de todas as fontes.</summary>
    public sealed class HistoryPage : UserControl, IRefreshable
    {
        private readonly MainWindow _host;
        private readonly StackPanel _list;
        private readonly TextBlock _subtitle;

        public HistoryPage(MainWindow host)
        {
            _host = host;
            var root = new StackPanel { Margin = new Thickness(34, 26, 34, 46) };

            var head = new DockPanel();
            var titles = new StackPanel();
            titles.Children.Add(Theme.H1("Histórico"));
            _subtitle = new TextBlock
            {
                Text = "Continue de onde parou",
                FontSize = 13,
                Foreground = Theme.Muted,
                Margin = new Thickness(2, 2, 0, 0),
            };
            titles.Children.Add(_subtitle);
            DockPanel.SetDock(titles, Dock.Left);
            head.Children.Add(titles);

            var clear = Theme.Btn("Limpar tudo", "DangerButton");
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            clear.VerticalAlignment = VerticalAlignment.Center;
            clear.Click += (_, _) =>
            {
                var answer = MessageBox.Show("Apagar todo o histórico de leitura?", "Solo Mangás",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                LocalStore.Instance.ClearHistory();
                _host.Toast("Histórico limpo.");
                Render();
            };
            DockPanel.SetDock(clear, Dock.Right);
            head.Children.Add(clear);
            root.Children.Add(head);

            root.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 16, 0, 0),
                Background = new LinearGradientBrush(Theme.AccentColor, Colors.Transparent, new Point(0, 0), new Point(1, 0)),
            });
            root.Children.Add(new Border { Height = 16 });

            _list = new StackPanel();
            root.Children.Add(_list);

            Content = Ui.Scroll(root);
            Render();
        }

        public void Refresh() => Render();

        private void Render()
        {
            _list.Children.Clear();
            var entries = LocalStore.Instance.History;
            if (entries.Count == 0)
            {
                _subtitle.Text = "Nada lido ainda";
                var box = new StackPanel { Margin = new Thickness(0, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                box.Children.Add(new TextBlock
                {
                    Text = "Nada por aqui ainda.",
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Theme.Text,
                });
                box.Children.Add(new TextBlock
                {
                    Text = "Abra um capítulo para começar; ele aparece aqui para retomar quando quiser.",
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

            var sources = entries.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var withChapter = entries.Count(e => !string.IsNullOrWhiteSpace(e.ChapterId));
            _subtitle.Text = entries.Count + " obra(s) · " + withChapter + " em andamento · " + sources + " fonte(s)";

            var lastBucket = string.Empty;
            foreach (var h in entries)
            {
                var entry = h;
                var bucket = Bucket(entry.Updated);
                if (bucket != lastBucket)
                {
                    lastBucket = bucket;
                    _list.Children.Add(Ui.Section(bucket));
                }
                _list.Children.Add(BuildRow(entry));
            }
        }

        private Border BuildRow(HistoryEntry entry)
        {
            var row = new Border
            {
                Background = Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand,
                ToolTip = "Abrir " + entry.MangaTitle,
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
                Text = string.IsNullOrWhiteSpace(entry.MangaTitle) ? "?" : entry.MangaTitle.Trim().Substring(0, 1).ToUpperInvariant(),
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
                Text = entry.MangaTitle,
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 640,
                HorizontalAlignment = HorizontalAlignment.Left,
            });

            var badges = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            badges.Children.Add(Badge(SourceRegistry.Instance.NameOf(entry.Source)));
            if (entry.Adult) badges.Children.Add(Badge("+18", Theme.DangerSoft));
            badges.Children.Add(new TextBlock
            {
                Text = entry.Updated.ToString("dd/MM HH:mm"),
                FontSize = 11,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
            });
            texts.Children.Add(badges);

            var chapterLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            chapterLine.Children.Add(new TextBlock
            {
                Text = entry.ChapterTitle,
                FontSize = 12,
                Foreground = Theme.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 340,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var pct = (int)Math.Round(Math.Max(0, Math.Min(1, entry.Position)) * 100);
            chapterLine.Children.Add(new TextBlock
            {
                Text = "  ·  " + pct + "%",
                FontSize = 11,
                Foreground = Theme.Accent2,
                VerticalAlignment = VerticalAlignment.Center,
            });
            texts.Children.Add(chapterLine);

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
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, Math.Min(1, entry.Position)), GridUnitType.Star) });
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - Math.Min(1, entry.Position)), GridUnitType.Star) });
            var fill = new Border { Background = Theme.Grad, CornerRadius = new CornerRadius(2) };
            Grid.SetColumn(fill, 0);
            fillGrid.Children.Add(fill);
            track.Child = fillGrid;
            texts.Children.Add(track);

            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var cont = Theme.Btn("Continuar", "PrimaryButton");
            cont.Margin = new Thickness(0, 0, 8, 0);
            cont.Click += (_, _) => _host.ShowManga(entry.Source, entry.MangaId, entry.Adult, entry.ChapterId);
            actions.Children.Add(cont);

            var open = Theme.Btn("Obra", "GhostButton");
            open.Margin = new Thickness(0, 0, 8, 0);
            open.ToolTip = "Abrir a página da obra";
            open.Click += (_, _) => _host.ShowManga(entry.Source, entry.MangaId, entry.Adult);
            actions.Children.Add(open);

            var remove = Theme.Btn("✕", "IconButton");
            remove.ToolTip = "Remover do histórico";
            remove.Click += (_, _) =>
            {
                LocalStore.Instance.RemoveHistory(entry.Source, entry.MangaId);
                _host.Toast("Removido do histórico.");
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
                _host.ShowManga(entry.Source, entry.MangaId, entry.Adult, entry.ChapterId);
            };
            return row;
        }

        private static Border Badge(string text, Brush bg = null) => new Border
        {
            Background = bg ?? Theme.AccentSoft,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Accent2,
            },
        };

        /// <summary>Agrupa o histórico por recência para facilitar a retomada.</summary>
        private static string Bucket(DateTime when)
        {
            var today = DateTime.Today;
            if (when.Date == today) return "Hoje";
            if (when.Date == today.AddDays(-1)) return "Ontem";
            if (when.Date >= today.AddDays(-7)) return "Esta semana";
            if (when.Date >= today.AddDays(-30)) return "Este mês";
            return "Anteriores";
        }
    }
}
