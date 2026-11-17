using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace SoloMangasNative
{
    public sealed class SettingsPage : UserControl, IAdultAware
    {
        private readonly MainWindow _host;
        private readonly StackPanel _sourcesPanel = new StackPanel();
        private Button _adultBtn;

        /// <summary>Linha de configuracao com titulo, descricao e area de acoes.</summary>
        private sealed class Row
        {
            public Border Box { get; }
            public TextBlock Subtitle { get; }
            public StackPanel Actions { get; } = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };

            public Row(string title, string subtitle)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var texts = new StackPanel();
                texts.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text });
                Subtitle = new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11.5,
                    Foreground = Theme.Muted,
                    Margin = new Thickness(0, 3, 14, 0),
                    TextWrapping = TextWrapping.Wrap,
                };
                texts.Children.Add(Subtitle);
                Grid.SetColumn(texts, 0);
                grid.Children.Add(texts);
                Grid.SetColumn(Actions, 1);
                grid.Children.Add(Actions);

                Box = new Border
                {
                    Background = Theme.Panel,
                    BorderBrush = Theme.Line,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 10),
                    Child = grid,
                };
            }
        }

        public SettingsPage(MainWindow host)
        {
            _host = host;
            var root = new StackPanel { Margin = new Thickness(34, 26, 34, 46) };
            root.Children.Add(Theme.H1("Configurações"));
            root.Children.Add(new TextBlock
            {
                Text = "Preferências locais do Solo Mangás 2.7",
                FontSize = 13,
                Foreground = Theme.Muted,
                Margin = new Thickness(2, 2, 0, 0),
            });
            root.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 16, 0, 0),
                Background = new LinearGradientBrush(Theme.AccentColor, Colors.Transparent, new Point(0, 0), new Point(1, 0)),
            });

            root.Children.Add(Ui.Section("Catálogo"));

            var adultRow = new Row("Conteúdo +18",
                "Mostra obras adultas nas fontes normais e libera as fontes exclusivamente +18. " +
                "O botão ＋18 da barra lateral alterna isso de qualquer tela.");
            _adultBtn = Theme.Btn(LocalStore.Instance.Settings.Adult ? "Ativado" : "Desativado",
                LocalStore.Instance.Settings.Adult ? "SoftButton" : "GhostButton");
            _adultBtn.Click += (_, _) => _host.ToggleAdult();
            adultRow.Actions.Children.Add(_adultBtn);
            root.Children.Add(adultRow.Box);

            var titleRow = new Row("Título preferido", "Português ou original, quando a fonte oferece os dois.");
            var titleBtn = Theme.Btn(LocalStore.Instance.Settings.TitlePreference == "original" ? "Original" : "Português", "GhostButton");
            titleBtn.Click += (_, _) =>
            {
                var s = LocalStore.Instance.Settings;
                s.TitlePreference = s.TitlePreference == "original" ? "pt" : "original";
                LocalStore.Instance.Save();
                titleBtn.Content = s.TitlePreference == "original" ? "Original" : "Português";
            };
            titleRow.Actions.Children.Add(titleBtn);
            root.Children.Add(titleRow.Box);

            var globalRow = new Row("Busca global", "Consulta todas as fontes de uma vez e agrupa obras com o mesmo título.");
            var globalBtn = Theme.Btn(LocalStore.Instance.Settings.GlobalSearch ? "Ativada" : "Desativada",
                LocalStore.Instance.Settings.GlobalSearch ? "SoftButton" : "GhostButton");
            globalBtn.Click += (_, _) =>
            {
                LocalStore.Instance.Settings.GlobalSearch = !LocalStore.Instance.Settings.GlobalSearch;
                LocalStore.Instance.Save();
                var on = LocalStore.Instance.Settings.GlobalSearch;
                globalBtn.Content = on ? "Ativada" : "Desativada";
                globalBtn.Style = (Style)Application.Current.Resources[on ? "SoftButton" : "GhostButton"];
            };
            globalRow.Actions.Children.Add(globalBtn);
            root.Children.Add(globalRow.Box);

            root.Children.Add(Ui.Section("Leitor"));

            var continuousRow = new Row("Leitura contínua", "Emenda o próximo capítulo no fim do atual, sem trocar de tela.");
            var continuousBtn = Theme.Btn(LocalStore.Instance.Settings.ContinuousRead ? "Ativado" : "Desativado",
                LocalStore.Instance.Settings.ContinuousRead ? "SoftButton" : "GhostButton");
            continuousBtn.Click += (_, _) =>
            {
                LocalStore.Instance.Settings.ContinuousRead = !LocalStore.Instance.Settings.ContinuousRead;
                LocalStore.Instance.Save();
                var on = LocalStore.Instance.Settings.ContinuousRead;
                continuousBtn.Content = on ? "Ativado" : "Desativado";
                continuousBtn.Style = (Style)Application.Current.Resources[on ? "SoftButton" : "GhostButton"];
            };
            continuousRow.Actions.Children.Add(continuousBtn);
            root.Children.Add(continuousRow.Box);

            var widthRow = new Row("Largura da página", "Compacta, confortável ou ampla. Automática acompanha o tamanho da janela.");
            var widthBtn = Theme.Btn(WidthLabel(), "GhostButton");
            widthBtn.Click += (_, _) =>
            {
                var s = LocalStore.Instance.Settings;
                s.ReaderWidth = (s.ReaderWidth + 1) % 4;
                LocalStore.Instance.Save();
                widthBtn.Content = WidthLabel();
            };
            widthRow.Actions.Children.Add(widthBtn);
            root.Children.Add(widthRow.Box);

            var modeRow = new Row("Modo de leitura", "Webtoon rola sem parar; página a página avança uma tela por vez.");
            var modeBtn = Theme.Btn(ReaderModeLabel(), "GhostButton");
            modeBtn.Click += (_, _) =>
            {
                var s = LocalStore.Instance.Settings;
                s.ReaderMode = s.ReaderMode == "page" ? "webtoon" : "page";
                LocalStore.Instance.Save();
                modeBtn.Content = ReaderModeLabel();
            };
            modeRow.Actions.Children.Add(modeBtn);
            root.Children.Add(modeRow.Box);

            root.Children.Add(Ui.Section("Dados"));

            var dataRow = new Row("Pasta de dados", LocalStore.Instance.DataDir);
            var openFolder = Theme.Btn("Abrir pasta", "GhostButton");
            openFolder.Click += (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(LocalStore.Instance.DataDir);
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + LocalStore.Instance.DataDir + "\"") { UseShellExecute = true });
                }
                catch (Exception ex) { _host.Toast(ex.Message, true); }
            };
            dataRow.Actions.Children.Add(openFolder);
            root.Children.Add(dataRow.Box);

            var backupRow = new Row("Backup dos dados", "Exporta favoritos, histórico, posições e preferências em um arquivo .json.");
            var backup = Theme.Btn("Exportar backup", "GhostButton");
            backup.Click += (_, _) =>
            {
                var dlg = new SaveFileDialog
                {
                    Title = "Salvar backup do Solo Mangás",
                    FileName = "solo-mangas-backup-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json",
                    Filter = "Backup JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
                    DefaultExt = ".json",
                    AddExtension = true,
                };
                if (dlg.ShowDialog() != true) return;
                try
                {
                    LocalStore.Instance.Flush();
                    File.Copy(LocalStore.Instance.StoreFile, dlg.FileName, true);
                    _host.Toast("Backup salvo em " + Path.GetFileName(dlg.FileName) + ".");
                }
                catch (Exception ex)
                {
                    _host.Toast("Falha ao exportar: " + ex.Message, true);
                }
            };
            backupRow.Actions.Children.Add(backup);
            root.Children.Add(backupRow.Box);

            var cacheRow = new Row("Cache de imagens", ImageLoader.DiskCacheSize() + " MB em disco — capas e páginas carregam mais rápido.");
            var clearCache = Theme.Btn("Limpar cache", "GhostButton");
            clearCache.Click += (_, _) =>
            {
                var n = ImageLoader.ClearDiskCache();
                cacheRow.Subtitle.Text = ImageLoader.DiskCacheSize() + " MB em disco — capas e páginas carregam mais rápido.";
                _host.Toast("Cache limpo: " + n + " arquivo(s) removido(s).");
            };
            cacheRow.Actions.Children.Add(clearCache);
            root.Children.Add(cacheRow.Box);

            var historyRow = new Row("Histórico de leitura", LocalStore.Instance.History.Count + " obra(s) no histórico.");
            var clearHistory = Theme.Btn("Limpar histórico", "DangerButton");
            clearHistory.Click += (_, _) =>
            {
                var answer = MessageBox.Show("Apagar todo o histórico de leitura?", "Solo Mangás",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                LocalStore.Instance.ClearHistory();
                _host.Toast("Histórico limpo.");
            };
            historyRow.Actions.Children.Add(clearHistory);
            root.Children.Add(historyRow.Box);

            var favRow = new Row("Favoritos", LocalStore.Instance.Favorites.Count + " obra(s) salva(s).");
            var clearFav = Theme.Btn("Limpar favoritos", "DangerButton");
            clearFav.Click += (_, _) =>
            {
                var answer = MessageBox.Show("Remover todas as obras dos favoritos?", "Solo Mangás",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                foreach (var f in LocalStore.Instance.Favorites.ToList())
                    LocalStore.Instance.RemoveFavorite(f.Source, f.Id);
                _host.Toast("Favoritos limpos.");
            };
            favRow.Actions.Children.Add(clearFav);
            root.Children.Add(favRow.Box);

            root.Children.Add(Ui.Section("Atualizações"));

            var versionRow = new Row("Versão instalada",
                "Solo Mangás " + UpdateService.CurrentLabel + " — verifique se há uma versão mais nova publicada.");
            var checkBtn = Theme.Btn("Verificar atualizações", "SoftButton");
            System.Windows.Automation.AutomationProperties.SetAutomationId(checkBtn, "update-check");
            checkBtn.Click += (_, _) =>
            {
                var w = new UpdateWindow(_host);
                w.ShowDialog();
            };
            versionRow.Actions.Children.Add(checkBtn);
            root.Children.Add(versionRow.Box);

            var startRow = new Row("Verificar ao iniciar",
                "Consulta a origem em segundo plano ao abrir o app e avisa quando houver atualização.");
            var startBtn = Theme.Btn(LocalStore.Instance.Settings.CheckUpdatesOnStartup ? "Ativado" : "Desativado",
                LocalStore.Instance.Settings.CheckUpdatesOnStartup ? "SoftButton" : "GhostButton");
            System.Windows.Automation.AutomationProperties.SetAutomationId(startBtn, "update-startup");
            startBtn.Click += (_, _) =>
            {
                LocalStore.Instance.Settings.CheckUpdatesOnStartup = !LocalStore.Instance.Settings.CheckUpdatesOnStartup;
                LocalStore.Instance.Save();
                var on = LocalStore.Instance.Settings.CheckUpdatesOnStartup;
                startBtn.Content = on ? "Ativado" : "Desativado";
                startBtn.Style = (Style)Application.Current.Resources[on ? "SoftButton" : "GhostButton"];
            };
            startRow.Actions.Children.Add(startBtn);
            root.Children.Add(startRow.Box);

            var sourceRow = new Row("Origem das atualizações",
                "URL do update.json, pasta local ou de rede. Vazio usa o update.json ao lado do executável.");
            var sourceBox = new TextBox
            {
                Text = LocalStore.Instance.Settings.UpdateSource,
                Width = 330,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Ex.: https://seusite.com/solo/update.json ou D:\\SoloMangas",
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(sourceBox, "update-source");
            sourceBox.TextChanged += (_, _) =>
            {
                LocalStore.Instance.Settings.UpdateSource = sourceBox.Text.Trim();
                LocalStore.Instance.SaveSoon();
            };
            sourceRow.Actions.Children.Add(sourceBox);
            root.Children.Add(sourceRow.Box);

            root.Children.Add(Ui.Section("Fontes"));
            root.Children.Add(_sourcesPanel);
            BuildSources();

            root.Children.Add(Ui.Section("Sobre"));
            root.Children.Add(new TextBlock
            {
                Text = "Solo Mangás 2.7 — aplicativo nativo em C# / WPF (.NET 8).\n" +
                       "Sem WebView2: telas, leitor, histórico e favoritos construídos em WPF.\n" +
                       "Fontes: Mangeek, Manhastro, Ler Mangá, MangaDex, MangaFlix e MangaLivre. " +
                       "Com o modo +18 ligado entram Hanami Heaven, HQ Brasa, Melhor Hentai e MangaDex +18, " +
                       "todas em português. A busca global consulta todas as fontes visíveis e agrupa títulos repetidos.\n" +
                       "Novidades da 2.7: cada capa é baixada uma única vez e reaproveitada em todos os tamanhos, " +
                       "as imagens carregam só quando aparecem na tela e a busca global mostra cada fonte assim que ela responde, " +
                       "com cache de resultados por 2,5 minutos. A tela inicial ganhou cabeçalho fixo, destaque mais leve e " +
                       "esqueletos de carregamento.\n" +
                       "Da 2.6: a página inicial da Mangeek carrega o catálogo completo conforme você rola.\n" +
                       "Da 2.5: verificação de atualizações, download com conferência de SHA-256 " +
                       "e troca automática do executável — o app fecha, se atualiza e reabre sozinho.\n" +
                       "Da 2.4: botão +18 global na barra lateral, fontes exclusivamente +18 em português " +
                       "e capítulos do Ler Mangá carregando pelas URLs originais das páginas.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Muted,
                FontSize = 13,
                LineHeight = 21,
                MaxWidth = 760,
            });

            Content = Ui.Scroll(root);
        }

        /// <summary>Modo +18 mudou: atualiza o botão e a lista de fontes visíveis.</summary>
        public void AdultChanged()
        {
            UpdateAdultButton();
            BuildSources();
        }

        private void UpdateAdultButton()
        {
            if (_adultBtn == null) return;
            var on = LocalStore.Instance.Settings.Adult;
            _adultBtn.Content = on ? "Ativado" : "Desativado";
            _adultBtn.Style = (Style)Application.Current.Resources[on ? "SoftButton" : "GhostButton"];
        }

        private void BuildSources()
        {
            _sourcesPanel.Children.Clear();
            foreach (var src in SourceRegistry.Instance.Enabled(LocalStore.Instance.Settings.Adult))
            {
                var s = src;
                var title = s.AdultOnly ? s.Name + "  ·  +18" : s.Name;
                var row = new Row(title, s.SupportsLogin ? "Requer login" : s.AdultOnly ? "Somente obras +18" : "Acesso público");
                var open = Theme.Btn("Abrir", "GhostButton");
                open.Click += (_, _) => _host.SetSource(s.Key);
                row.Actions.Children.Add(open);
                _sourcesPanel.Children.Add(row.Box);
            }
        }

        private static string WidthLabel()
        {
            switch (LocalStore.Instance.Settings.ReaderWidth)
            {
                case 1: return "Compacta";
                case 2: return "Confortável";
                case 3: return "Ampla";
                default: return "Automática";
            }
        }

        private static string ReaderModeLabel() =>
            LocalStore.Instance.Settings.ReaderMode == "page" ? "Página a página" : "Webtoon";

    }
}
