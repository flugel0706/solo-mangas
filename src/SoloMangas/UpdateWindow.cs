using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoloMangasNative
{
    /// <summary>
    /// Janela de atualizações: verifica o manifesto, mostra as notas da nova
    /// versão, baixa com progresso e aplica a troca do executável em uso.
    /// </summary>
    public sealed class UpdateWindow : Window
    {
        private enum Stage { Checking, Available, Downloading, Ready, UpToDate, Failed }

        private static bool _anyOpen;
        public static bool AnyOpen => _anyOpen;

        private readonly UpdateInfo _preset;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly TextBlock _status;
        private readonly TextBlock _detail;
        private readonly TextBlock _notes;
        private readonly Border _notesBox;
        private readonly ProgressBar _bar;
        private readonly Button _primary;
        private readonly Button _secondary;
        private readonly TextBlock _sourceLine;

        private UpdateInfo _info;
        private string _readyPath;
        private Stage _stage = Stage.Checking;

        public UpdateWindow(MainWindow host, UpdateInfo preset = null)
        {
            _preset = preset;
            _anyOpen = true;

            Title = "Atualizações — Solo Mangás";
            Width = 640;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 760;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = Theme.Bg;
            Foreground = Theme.Text;
            FontFamily = Theme.Body;
            Owner = host;

            var root = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x12, 0x08, 0x0C), Color.FromRgb(0x08, 0x04, 0x06),
                    new Point(0, 0), new Point(0, 1)),
                Padding = new Thickness(26, 22, 26, 24),
            };

            var content = new StackPanel();
            root.Child = content;

            // ---------------- Cabeçalho ----------------
            var header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel();
            titles.Children.Add(new TextBlock
            {
                Text = "Atualizações",
                FontSize = 25,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = Theme.Text,
            });
            titles.Children.Add(new TextBlock
            {
                Text = "Solo Mangás " + UpdateService.CurrentLabel + " instalado",
                FontSize = 12,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 3, 0, 0),
            });
            Grid.SetColumn(titles, 0);
            header.Children.Add(titles);

            var close = Theme.Btn("✕", "IconButton");
            close.Width = 36;
            close.Height = 30;
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Click += (_, _) => Close();
            AutomationProperties.SetAutomationId(close, "update-close");
            Grid.SetColumn(close, 1);
            header.Children.Add(close);

            content.Children.Add(header);

            // ---------------- Status ----------------
            _status = new TextBlock
            {
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 18, 0, 0),
            };
            content.Children.Add(_status);

            _detail = new TextBlock
            {
                FontSize = 12.5,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
                Margin = new Thickness(0, 6, 0, 0),
            };
            content.Children.Add(_detail);

            // ---------------- Notas da versão ----------------
            _notes = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Text,
                FontSize = 13,
                LineHeight = 20,
            };
            var notesScroll = new ScrollViewer
            {
                MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _notes,
            };
            _notesBox = new Border
            {
                Background = Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 15, 0, 0),
                Child = notesScroll,
                Visibility = Visibility.Collapsed,
            };
            content.Children.Add(_notesBox);

            // ---------------- Progresso ----------------
            _bar = new ProgressBar
            {
                Height = 8,
                Minimum = 0,
                Maximum = 1,
                Margin = new Thickness(0, 16, 0, 0),
                Foreground = Theme.Accent,
                Background = Theme.Panel3,
                BorderThickness = new Thickness(0),
                Visibility = Visibility.Collapsed,
            };
            content.Children.Add(_bar);

            // ---------------- Origem / botões ----------------
            _sourceLine = new TextBlock
            {
                FontSize = 10.5,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = "Manifesto de atualização consultado",
                Margin = new Thickness(0, 14, 0, 0),
                Visibility = Visibility.Collapsed,
            };
            content.Children.Add(_sourceLine);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
            };

            _secondary = Theme.Btn("Fechar", "GhostButton");
            _secondary.MinWidth = 104;
            _secondary.Click += (_, _) => OnSecondary();
            AutomationProperties.SetAutomationId(_secondary, "update-secondary");
            buttons.Children.Add(_secondary);

            _primary = Theme.Btn("Baixar e instalar", "PrimaryButton");
            _primary.MinWidth = 186;
            _primary.Margin = new Thickness(10, 0, 0, 0);
            _primary.Click += (_, _) => OnPrimary();
            AutomationProperties.SetAutomationId(_primary, "update-primary");
            buttons.Children.Add(_primary);

            content.Children.Add(buttons);

            Content = root;

            Loaded += async (_, _) =>
            {
                if (_preset != null) ShowAvailable(_preset);
                else await CheckAsync();
            };
            Closed += (_, _) =>
            {
                _anyOpen = false;
                try { _cts.Cancel(); } catch { }
            };
        }

        // ------------------------------------------------------------------
        // Fluxo
        // ------------------------------------------------------------------

        private async Task CheckAsync()
        {
            ResetToken();
            ShowChecking();
            try
            {
                var info = await UpdateService.CheckAsync(LocalStore.Instance.Settings.UpdateSource, _cts.Token);
                if (info.Parsed > UpdateService.CurrentVersion) ShowAvailable(info);
                else ShowUpToDate(info);
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private async Task DownloadAsync()
        {
            if (_info == null) return;
            ResetToken();
            ShowDownloading();

            var progress = new Progress<double>(p =>
            {
                _bar.IsIndeterminate = false;
                _bar.Value = p;
                _detail.Text = (p * 100).ToString("0") + "%" +
                    (_info.Size > 0 ? " de " + SizeLabel(_info) : string.Empty);
            });

            try
            {
                _readyPath = await UpdateService.DownloadAsync(_info, progress, _cts.Token);
                ShowReady();
            }
            catch (OperationCanceledException)
            {
                if (_info != null) ShowAvailable(_info);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void Apply()
        {
            if (string.IsNullOrEmpty(_readyPath) || _info == null) return;

            var answer = MessageBox.Show(this,
                "O Solo Mangás vai fechar, ser substituído pela versão " + _info.Version +
                " e reabrir automaticamente.\n\nContinuar?",
                "Atualizar agora", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            if (!UpdateService.LaunchApplier(_readyPath, out var error))
            {
                ShowError(error);
                return;
            }

            LocalStore.Instance.Flush();
            Application.Current.Shutdown();
        }

        // ------------------------------------------------------------------
        // Estados da janela
        // ------------------------------------------------------------------

        private void ShowChecking()
        {
            _stage = Stage.Checking;
            _status.Text = "Procurando atualizações…";
            _status.Foreground = Theme.Text;
            _detail.Text = "Versão instalada: Solo Mangás " + UpdateService.CurrentLabel + ".";
            _notesBox.Visibility = Visibility.Collapsed;
            _bar.Visibility = Visibility.Visible;
            _bar.IsIndeterminate = true;
            _sourceLine.Text = "Manifesto: " + ManifestDisplay();
            _sourceLine.Visibility = Visibility.Visible;

            _primary.Visibility = Visibility.Collapsed;
            _secondary.Content = "Fechar";
            _secondary.Style = (Style)Application.Current.Resources["GhostButton"];
            _secondary.IsEnabled = true;
        }

        private void ShowAvailable(UpdateInfo info)
        {
            _info = info;
            _stage = Stage.Available;

            _status.Text = "Versão " + info.Version + " disponível";
            _status.Foreground = Theme.Text;
            _detail.Text = "Você tem a " + UpdateService.CurrentLabel +
                (info.Size > 0 ? " · download de " + SizeLabel(info) : string.Empty) + ".";

            _notes.Text = string.IsNullOrWhiteSpace(info.Notes)
                ? "Sem notas desta versão."
                : info.Notes;
            _notesBox.Visibility = Visibility.Visible;

            _bar.Visibility = Visibility.Collapsed;
            _bar.IsIndeterminate = false;
            _bar.Value = 0;

            _sourceLine.Text = "Manifesto: " + info.Manifest;
            _sourceLine.Visibility = Visibility.Visible;

            _primary.Content = "Baixar e instalar";
            _primary.Style = (Style)Application.Current.Resources["PrimaryButton"];
            _primary.Visibility = Visibility.Visible;
            _primary.IsEnabled = true;

            _secondary.Content = "Depois";
            _secondary.Style = (Style)Application.Current.Resources["GhostButton"];
            _secondary.IsEnabled = true;
        }

        private void ShowDownloading()
        {
            _stage = Stage.Downloading;
            _status.Text = "Baixando a versão " + (_info?.Version ?? string.Empty) + "…";
            _status.Foreground = Theme.Text;
            _detail.Text = "0%";
            _notesBox.Visibility = Visibility.Collapsed;
            _bar.Visibility = Visibility.Visible;
            _bar.IsIndeterminate = false;
            _bar.Value = 0;
            _sourceLine.Visibility = Visibility.Visible;

            _primary.Content = "Baixando…";
            _primary.Visibility = Visibility.Visible;
            _primary.IsEnabled = false;

            _secondary.Content = "Cancelar";
            _secondary.Style = (Style)Application.Current.Resources["GhostButton"];
            _secondary.IsEnabled = true;
        }

        private void ShowReady()
        {
            _stage = Stage.Ready;
            _status.Text = "Atualização pronta";
            _status.Foreground = Theme.Good;
            _detail.Text = "O app vai fechar, ser substituído pela versão " + _info.Version +
                " e reabrir automaticamente.";
            _notesBox.Visibility = Visibility.Collapsed;
            _bar.Visibility = Visibility.Visible;
            _bar.IsIndeterminate = false;
            _bar.Value = 1;

            _primary.Content = "Reiniciar e atualizar";
            _primary.Style = (Style)Application.Current.Resources["PrimaryButton"];
            _primary.Visibility = Visibility.Visible;
            _primary.IsEnabled = true;

            _secondary.Content = "Depois";
            _secondary.Style = (Style)Application.Current.Resources["GhostButton"];
            _secondary.IsEnabled = true;
        }

        private void ShowUpToDate(UpdateInfo info)
        {
            _info = info;
            _stage = Stage.UpToDate;

            _status.Text = "Você está na versão mais recente.";
            _status.Foreground = Theme.Good;
            _detail.Text = "Solo Mangás " + UpdateService.CurrentLabel + " · nenhuma atualização disponível.";
            _notesBox.Visibility = Visibility.Collapsed;
            _bar.Visibility = Visibility.Collapsed;

            _sourceLine.Text = "Manifesto: " + info.Manifest;
            _sourceLine.Visibility = Visibility.Visible;

            _primary.Visibility = Visibility.Collapsed;
            _secondary.Content = "Fechar";
            _secondary.Style = (Style)Application.Current.Resources["GhostButton"];
            _secondary.IsEnabled = true;
        }

        private void ShowError(string message)
        {
            _stage = Stage.Failed;
            _status.Text = "Não foi possível atualizar.";
            _status.Foreground = Theme.Accent2;
            _detail.Text = message;
            _notesBox.Visibility = Visibility.Collapsed;
            _bar.Visibility = Visibility.Collapsed;
            _sourceLine.Text = "Manifesto: " + ManifestDisplay();
            _sourceLine.Visibility = Visibility.Visible;

            _primary.Content = "Tentar novamente";
            _primary.Style = (Style)Application.Current.Resources["PrimaryButton"];
            _primary.Visibility = Visibility.Visible;
            _primary.IsEnabled = true;

            _secondary.Content = "Fechar";
            _secondary.Style = (Style)Application.Current.Resources["GhostButton"];
            _secondary.IsEnabled = true;
        }

        // ------------------------------------------------------------------
        // Ações
        // ------------------------------------------------------------------

        private void OnPrimary()
        {
            switch (_stage)
            {
                case Stage.Available: _ = DownloadAsync(); break;
                case Stage.Ready: Apply(); break;
                case Stage.Failed: _ = CheckAsync(); break;
            }
        }

        private void OnSecondary()
        {
            if (_stage == Stage.Downloading)
            {
                try { _cts.Cancel(); } catch { }
                return;
            }
            Close();
        }

        private void ResetToken()
        {
            try { _cts.Cancel(); } catch { }
            try { _cts.Dispose(); } catch { }
            _cts = new CancellationTokenSource();
        }

        private string ManifestDisplay()
        {
            if (_info != null && !string.IsNullOrEmpty(_info.Manifest)) return _info.Manifest;
            var source = (LocalStore.Instance.Settings.UpdateSource ?? string.Empty).Trim();
            if (source.Length == 0)
            {
                var dir = System.IO.Path.GetDirectoryName(UpdateService.TargetExe);
                return System.IO.Path.Combine(dir ?? string.Empty, "update.json");
            }
            return source;
        }

        private static string SizeLabel(UpdateInfo info) =>
            info.Size > 0 ? (info.Size / 1048576.0).ToString("0.0") + " MB" : "tamanho desconhecido";
    }
}
