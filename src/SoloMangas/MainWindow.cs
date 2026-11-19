using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SoloMangasNative
{
    public sealed class MainWindow : Window
    {
        private readonly ContentControl _host = new ContentControl();
        private readonly Border _toast;
        private readonly TextBlock _toastText;
        private readonly DispatcherTimer _toastTimer;
        private readonly StackPanel _navPanel = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        private readonly StackPanel _sourcePanel = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        private readonly ColumnDefinition _sidebarCol;
        private readonly Border _sidebar;
        private readonly TextBlock _accountLine;
        private readonly Dictionary<string, Button> _navButtons = new Dictionary<string, Button>();
        private readonly Dictionary<Button, (Ellipse Dot, TextBlock Label)> _sourceVisuals =
            new Dictionary<Button, (Ellipse, TextBlock)>();
        private readonly Dictionary<string, Button> _sourceButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private Button _adultBtn;
        private string _activeNav = "home";
        private string _sourceKey;

        private bool _fullscreen;
        private bool _pageImmersive;
        private WindowStyle _oldStyle;
        private ResizeMode _oldResize;
        private WindowState _oldState;
        private Rect _oldBounds;
        private Grid _root;
        private readonly List<(UIElement Page, string NavKey)> _back = new List<(UIElement, string)>();

        public ISource Current => SourceRegistry.Instance.Get(_sourceKey);
        public bool IsFullscreen => _fullscreen;

        public MainWindow()
        {
            Title = "Solo Mangás " + UpdateService.CurrentLabel;
            var area = SystemParameters.WorkArea;
            Width = Math.Max(900, Math.Min(1360, area.Width - 24));
            Height = Math.Max(600, Math.Min(900, area.Height - 24));
            MinWidth = 820;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.Bg;
            Foreground = Theme.Text;
            FontFamily = Theme.Body;

            RestoreWindowGeometry(area);

            _sourceKey = LocalStore.Instance.Settings.LastSource;
            if (SourceRegistry.Instance.Enabled(LocalStore.Instance.Settings.Adult).All(s => s.Key != _sourceKey))
                _sourceKey = "mangeek";

            _root = new Grid { Background = Theme.Bg };
            _root.ColumnDefinitions.Add(_sidebarCol = new ColumnDefinition { Width = new GridLength(252) });
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Papel de parede do tema Call of Duty: imagem real por baixo de
            // tudo, com película escura para manter a leitura confortável.
            if (Theme.HasWallpaper && Theme.Wallpaper != null)
            {
                var wallpaper = new Image
                {
                    Source = Theme.Wallpaper,
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false,
                    Opacity = 0.95,
                };
                RenderOptions.SetBitmapScalingMode(wallpaper, BitmapScalingMode.HighQuality);
                Grid.SetColumnSpan(wallpaper, 2);
                _root.Children.Add(wallpaper);

                // A película escurece mais a esquerda (onde ficam os textos) e
                // deixa o por do sol e a silhueta aparecerem à direita.
                var shade = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0xE0, 0x05, 0x08, 0x05), 0),
                        new GradientStop(Color.FromArgb(0xA2, 0x06, 0x0A, 0x06), 0.32),
                        new GradientStop(Color.FromArgb(0x7A, 0x07, 0x0B, 0x07), 0.66),
                        new GradientStop(Color.FromArgb(0xAC, 0x03, 0x05, 0x03), 1),
                    },
                };
                shade.Freeze();
                var dim = new Border { Background = shade, IsHitTestVisible = false };
                Grid.SetColumnSpan(dim, 2);
                _root.Children.Add(dim);

                // Linhas finas de "visor" por cima do fundo, bem discretas.
                var scan = new DrawingBrush
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 4, 4),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Drawing = new GeometryDrawing(
                        null,
                        new Pen(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), 1),
                        new LineGeometry(new Point(0, 0.5), new Point(4, 0.5))),
                };
                scan.Freeze();
                var scanlines = new Rectangle { Fill = scan, IsHitTestVisible = false };
                Grid.SetColumnSpan(scanlines, 2);
                _root.Children.Add(scanlines);
            }

            // Halo de acento suave no canto superior esquerdo: dá profundidade ao tema.
            var glow = new Border
            {
                IsHitTestVisible = false,
                Background = new RadialGradientBrush
                {
                    Center = new Point(0.05, 0.02),
                    GradientOrigin = new Point(0.05, 0.02),
                    RadiusX = 0.85,
                    RadiusY = 0.7,
                    GradientStops =
                    {
                        new GradientStop(Theme.Glow1Color, 0),
                        new GradientStop(Color.FromArgb(0x00, Theme.Glow1Color.R, Theme.Glow1Color.G, Theme.Glow1Color.B), 0.62),
                    },
                },
            };
            Grid.SetColumnSpan(glow, 2);
            _root.Children.Add(glow);

            // Segundo halo, mais frio e discreto, no canto inferior direito.
            var glow2 = new Border
            {
                IsHitTestVisible = false,
                Background = new RadialGradientBrush
                {
                    Center = new Point(0.96, 0.98),
                    GradientOrigin = new Point(0.96, 0.98),
                    RadiusX = 0.55,
                    RadiusY = 0.5,
                    GradientStops =
                    {
                        new GradientStop(Theme.Glow2Color, 0),
                        new GradientStop(Color.FromArgb(0x00, Theme.Glow2Color.R, Theme.Glow2Color.G, Theme.Glow2Color.B), 0.7),
                    },
                },
            };
            Grid.SetColumnSpan(glow2, 2);
            _root.Children.Add(glow2);

            // ---------------- Sidebar ----------------
            _sidebar = new Border
            {
                Background = Theme.Sidebar,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(0, 0, 1, 0),
            };
            var side = new DockPanel { LastChildFill = true };

            var logo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 14, 14, 14) };
            logo.Children.Add(new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(12),
                Background = Theme.Grad,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.55,
                    Color = Theme.AccentColor,
                },
                Child = new TextBlock
                {
                    Text = "S",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    FontFamily = Theme.Display,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
            var logoText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            logoText.Children.Add(new TextBlock
            {
                Text = "SOLO",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                FontFamily = Theme.Display,
                Foreground = Theme.Text,
                LineHeight = 18,
            });
            logoText.Children.Add(new TextBlock
            {
                Text = "MANGÁS",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Accent2,
                LineHeight = 14,
            });
            logo.Children.Add(logoText);
            DockPanel.SetDock(logo, Dock.Top);
            side.Children.Add(logo);

            AddNav("home", "⌂", "Início");
            AddNav("search", "⌕", "Buscar");
            AddNav("favorites", "♥", "Favoritos");
            AddNav("history", "↺", "Histórico");

            var navScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _navPanel,
            };
            DockPanel.SetDock(navScroll, Dock.Top);
            side.Children.Add(navScroll);

            var srcTitle = new TextBlock
            {
                Text = "FONTES",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.Muted,
                Margin = new Thickness(26, 16, 0, 6),
            };
            DockPanel.SetDock(srcTitle, Dock.Top);
            side.Children.Add(srcTitle);
            var srcScroll = new ScrollViewer
            {
                MinHeight = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _sourcePanel,
            };

            var footer = new StackPanel { Margin = new Thickness(20, 2, 20, 10) };
            footer.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 0, 0, 6) });
            _accountLine = new TextBlock
            {
                FontSize = 11.5,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 0, 4),
            };
            footer.Children.Add(_accountLine);

            // Botão global +18: alterna o modo adulto de qualquer tela e
            // mostra/oculta as fontes exclusivamente +18 na hora.
            _adultBtn = Theme.Btn("＋18   Modo +18: desativado", "NavButton");
            _adultBtn.HorizontalContentAlignment = HorizontalAlignment.Left;
            _adultBtn.Padding = new Thickness(15, 6, 15, 6);
            _adultBtn.Margin = new Thickness(0, 0, 0, 4);
            _adultBtn.Click += (_, _) => ToggleAdult();
            footer.Children.Add(_adultBtn);

            var settingsBtn = Theme.Btn("⚙   Configurações", "NavButton");
            settingsBtn.HorizontalContentAlignment = HorizontalAlignment.Left;
            settingsBtn.Padding = new Thickness(15, 6, 15, 6);
            settingsBtn.Click += (_, _) => ShowSettings();
            footer.Children.Add(settingsBtn);
            _navButtons["settings"] = settingsBtn;

            var accountBtn = Theme.Btn("◍   Conta", "NavButton");
            accountBtn.HorizontalContentAlignment = HorizontalAlignment.Left;
            accountBtn.Padding = new Thickness(15, 6, 15, 6);
            accountBtn.Click += (_, _) => ShowLogin();
            footer.Children.Add(accountBtn);
            _navButtons["account"] = accountBtn;

            footer.Children.Add(new TextBlock
            {
                Text = "v" + UpdateService.CurrentLabel + "  ·  " + Theme.Name,
                FontSize = 10.5,
                Foreground = Theme.Muted,
                Margin = new Thickness(4, 4, 0, 0),
            });
            DockPanel.SetDock(footer, Dock.Bottom);
            side.Children.Add(footer);

            // A lista de fontes ocupa toda a altura restante (cresce em 2.4:
            // 10 fontes com o modo +18 ligado) e rola quando a janela encolhe.
            side.Children.Add(srcScroll);

            _sidebar.Child = side;
            Grid.SetColumn(_sidebar, 0);
            _root.Children.Add(_sidebar);

            // ---------------- Conteúdo ----------------
            var contentRoot = new Grid();
            contentRoot.Background = Brushes.Transparent;
            _host.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _host.VerticalContentAlignment = VerticalAlignment.Stretch;
            contentRoot.Children.Add(_host);

            _toastText = new TextBlock { Foreground = Theme.Text, FontSize = 13, TextWrapping = TextWrapping.Wrap };
            _toast = new Border
            {
                Background = Theme.Toast,
                BorderBrush = Theme.AccentLine,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 11, 18, 11),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 28),
                Visibility = Visibility.Collapsed,
                Child = _toastText,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 22,
                    ShadowDepth = 4,
                    Opacity = 0.45,
                    Color = Colors.Black,
                },
            };
            contentRoot.Children.Add(_toast);

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
            _toastTimer.Tick += (_, _) => { _toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };

            Grid.SetColumn(contentRoot, 1);
            _root.Children.Add(contentRoot);
            Content = _root;

            BuildSourceList();
            UpdateAdultButton();
            UpdateAccount();
            Loaded += (_, _) =>
            {
                ShowHome();
                ImageLoader.TrimDiskCacheAsync();
                _ = CheckUpdatesOnStartupAsync();
            };
            SourceInitialized += (_, _) => ApplyDarkTitleBar();
            PreviewKeyDown += OnWindowKeyDown;
            PreviewMouseDown += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.XButton1 && GoBack()) e.Handled = true;
            };
            Closing += (_, _) =>
            {
                SaveWindowGeometry();
                LocalStore.Instance.Flush();
            };
        }

        // ------------------------------------------------------------------
        // Janela / tela cheia
        // ------------------------------------------------------------------
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// Deixa a barra de título escura e os cantos arredondados (Windows 11),
        /// para a janela combinar com o tema em vez do cinza padrão.
        /// </summary>
        private void ApplyDarkTitleBar()
        {
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var on = 1;
                if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));

                // DWMWA_WINDOW_CORNER_PREFERENCE (33) = DWMWCP_ROUND (2)
                var round = 2;
                DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
            }
            catch
            {
                // versões antigas do Windows não suportam; a barra fica padrão
            }
        }

        private void RestoreWindowGeometry(Rect area)
        {
            var s = LocalStore.Instance.Settings;
            if (s.WindowW >= 820 && s.WindowH >= 560 &&
                s.WindowW <= area.Width + 100 && s.WindowH <= area.Height + 100)
            {
                Width = s.WindowW;
                Height = s.WindowH;
            }
            if (s.WindowMaximized) WindowState = WindowState.Maximized;
        }

        private void SaveWindowGeometry()
        {
            var s = LocalStore.Instance.Settings;
            if (WindowState == WindowState.Maximized)
            {
                s.WindowMaximized = true;
                s.WindowW = RestoreBounds.Width;
                s.WindowH = RestoreBounds.Height;
            }
            else if (WindowState == WindowState.Normal)
            {
                s.WindowMaximized = false;
                s.WindowW = Width;
                s.WindowH = Height;
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && _fullscreen)
            {
                ExitFullscreen();
                e.Handled = true;
                return;
            }

            // Voltar: Alt+Esquerda, botão lateral do mouse ou tecla Voltar.
            if ((e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) ||
                e.Key == Key.BrowserBack)
            {
                if (GoBack()) e.Handled = true;
                return;
            }

            // Atalhos globais do shell. No leitor imersivo ficam desligados para
            // não tirar o usuário da leitura sem querer.
            if (!_pageImmersive && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.F || e.Key == Key.K) { ShowCatalog(focus: true); e.Handled = true; return; }
                if (e.Key == Key.H) { ShowHistory(); e.Handled = true; return; }
                if (e.Key == Key.OemComma || e.Key == Key.OemQuestion) { ShowSettings(); e.Handled = true; return; }
            }
        }

        /// <summary>
        /// F11 de verdade: remove a moldura da janela e ocupa a tela inteira.
        /// A barra de tarefas some; a barra de título também.
        /// </summary>
        public void ToggleFullscreen()
        {
            if (_fullscreen) ExitFullscreen();
            else EnterFullscreen();
        }

        private void EnterFullscreen()
        {
            if (_fullscreen) return;
            _oldStyle = WindowStyle;
            _oldResize = ResizeMode;
            _oldState = WindowState;
            _oldBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            // Precisa voltar a Normal antes de remover a moldura; senão o WPF
            // mantém a área de trabalho antiga e sobram bordas visíveis.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            // Cobre o monitor inteiro (inclusive a barra de tarefas), sem bordas.
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var monitor = MonitorFromWindow(handle, 2);
                var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    var sx = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
                    var sy = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
                    Left = info.rcMonitor.Left / sx;
                    Top = info.rcMonitor.Top / sy;
                    Width = Math.Max(320, (info.rcMonitor.Right - info.rcMonitor.Left) / sx);
                    Height = Math.Max(240, (info.rcMonitor.Bottom - info.rcMonitor.Top) / sy);
                }
                else
                {
                    WindowState = WindowState.Maximized;
                }
            }
            catch
            {
                WindowState = WindowState.Maximized;
            }

            Topmost = true;   // fica acima da barra de tarefas
            _fullscreen = true;
            SetImmersive(true);
        }

        private void ExitFullscreen()
        {
            if (!_fullscreen) return;
            _fullscreen = false;
            Topmost = false;
            WindowStyle = _oldStyle;
            ResizeMode = _oldResize;
            WindowState = WindowState.Normal;
            Left = _oldBounds.Left;
            Top = _oldBounds.Top;
            Width = _oldBounds.Width;
            Height = _oldBounds.Height;
            if (_oldState == WindowState.Maximized) WindowState = WindowState.Maximized;
            SetImmersive(_pageImmersive);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        // ------------------------------------------------------------------
        // Sidebar
        // ------------------------------------------------------------------
        private void AddNav(string key, string icon, string label)
        {
            var b = Theme.Btn(icon + "   " + label, "NavButton");
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Padding = new Thickness(15, 6, 15, 6);
            b.Margin = new Thickness(0, 1, 0, 1);
            b.Click += (_, _) =>
            {
                switch (key)
                {
                    case "home": ShowHome(); break;
                    case "search": ShowCatalog(); break;
                    case "favorites": ShowFavorites(); break;
                    case "history": ShowHistory(); break;
                }
            };
            _navButtons[key] = b;
            _navPanel.Children.Add(b);
        }

        private void BuildSourceList()
        {
            _sourcePanel.Children.Clear();
            _sourceVisuals.Clear();
            _sourceButtons.Clear();

            var adult = LocalStore.Instance.Settings.Adult;
            var headerAdded = false;
            foreach (var src in SourceRegistry.Instance.Enabled(adult))
            {
                var s = src;
                if (s.AdultOnly && !headerAdded)
                {
                    headerAdded = true;
                    _sourcePanel.Children.Add(new TextBlock
                    {
                        Text = "FONTES +18",
                        FontSize = 10.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = Theme.Accent2,
                        Margin = new Thickness(4, 6, 0, 0),
                    });
                }

                var b = Theme.Btn(string.Empty, "NavButton");
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Padding = new Thickness(12, 5, 12, 5);
                b.Margin = new Thickness(0, 1, 0, 1);
                System.Windows.Automation.AutomationProperties.SetName(b, s.Name);
                System.Windows.Automation.AutomationProperties.SetAutomationId(b, "source-" + s.Key);

                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Theme.Line,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(1, 0, 11, 0),
                };
                var label = new TextBlock
                {
                    Text = s.Name,
                    FontSize = 13,
                    Foreground = Theme.Muted,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(dot);
                row.Children.Add(label);
                b.Content = row;

                b.Click += (_, _) => SetSource(s.Key);
                _sourcePanel.Children.Add(b);
                _sourceVisuals[b] = (dot, label);
                _sourceButtons[s.Key] = b;
                ApplySourceStyle(b, string.Equals(s.Key, _sourceKey, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Repinta os botões de fonte pelo key (a lista pode ter cabeçalho e itens ocultos).</summary>
        private void RefreshSourceStyles()
        {
            foreach (var kv in _sourceButtons)
                ApplySourceStyle(kv.Value, string.Equals(kv.Key, _sourceKey, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------
        // Modo +18 global
        // ------------------------------------------------------------------
        public bool AdultMode => LocalStore.Instance.Settings.Adult;

        public void ToggleAdult() => SetAdult(!LocalStore.Instance.Settings.Adult);

        /// <summary>
        /// Liga/desliga o modo +18 em qualquer tela: refaz a lista de fontes,
        /// avisa a página atual e, se a fonte ativa for exclusiva +18, volta ao Início.
        /// </summary>
        public void SetAdult(bool on)
        {
            var s = LocalStore.Instance.Settings;
            if (s.Adult == on)
            {
                UpdateAdultButton();
                return;
            }

            s.Adult = on;
            LocalStore.Instance.Save();
            UpdateAdultButton();
            BuildSourceList();

            var current = SourceRegistry.Instance.Get(_sourceKey);
            if (!on && current.AdultOnly)
            {
                _sourceKey = "mangeek";
                s.LastSource = "mangeek";
                LocalStore.Instance.SaveSoon();
                RefreshSourceStyles();
                ShowHome();
                Toast("Fontes +18 ocultas.");
                return;
            }

            RefreshSourceStyles();
            if (_host.Content is IAdultAware aware) aware.AdultChanged();
            else if (_host.Content is IRefreshable refreshable) refreshable.Refresh();

            Toast(on ? "Fontes +18 liberadas." : "Fontes +18 ocultas.");
        }

        private void UpdateAdultButton()
        {
            if (_adultBtn == null) return;
            var on = LocalStore.Instance.Settings.Adult;
            _adultBtn.Content = on ? "＋18   Modo +18: ativado" : "＋18   Modo +18: desativado";
            _adultBtn.Background = on ? Theme.AccentSoft : Brushes.Transparent;
            _adultBtn.BorderBrush = on ? Theme.Accent2 : Brushes.Transparent;
            _adultBtn.Foreground = on ? Theme.Text : Theme.Muted;
            _adultBtn.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            _adultBtn.ToolTip = on
                ? "Ocultar obras e fontes +18 (continua nas fontes normais)"
                : "Mostrar obras e fontes exclusivamente +18";
        }

        private void ApplySourceStyle(Button b, bool active)
        {
            b.Background = active ? Theme.AccentSoft : Brushes.Transparent;
            b.BorderBrush = active ? Theme.Accent2 : Brushes.Transparent;
            if (_sourceVisuals.TryGetValue(b, out var v))
            {
                v.Dot.Fill = active ? Theme.Accent2 : Theme.Line;
                v.Label.Foreground = active ? Theme.Text : Theme.Muted;
                v.Label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        public void SetSource(string key)
        {
            if (string.Equals(_sourceKey, key, StringComparison.OrdinalIgnoreCase)) return;
            var source = SourceRegistry.Instance.Get(key);
            if (source.AdultOnly && !LocalStore.Instance.Settings.Adult) SetAdult(true);

            _sourceKey = key;
            LocalStore.Instance.Settings.LastSource = key;
            LocalStore.Instance.SaveSoon();
            RefreshSourceStyles();
            ShowHome();
        }

        /// <summary>Alinha a fonte ativa sem navegar (ex.: obra aberta pelo Histórico).</summary>
        public void SyncSource(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            var hit = SourceRegistry.Instance.Sources.FirstOrDefault(s =>
                string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
            if (hit == null) return;
            if (hit.AdultOnly && !LocalStore.Instance.Settings.Adult) SetAdult(true);
            if (string.Equals(_sourceKey, key, StringComparison.OrdinalIgnoreCase)) return;

            _sourceKey = key;
            LocalStore.Instance.Settings.LastSource = key;
            LocalStore.Instance.SaveSoon();
            RefreshSourceStyles();
        }

        private void SetActiveNav(string key)
        {
            _activeNav = key;
            foreach (var kv in _navButtons)
            {
                var active = kv.Key == key;
                kv.Value.Background = active ? Theme.AccentSoft : Brushes.Transparent;
                kv.Value.BorderBrush = active ? Theme.Accent : Brushes.Transparent;
                kv.Value.Foreground = active ? Theme.Accent2 : Theme.Muted;
                kv.Value.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        public void UpdateAccount()
        {
            var session = SourceRegistry.Instance.Session;
            if (string.IsNullOrEmpty(session.Token))
            {
                _accountLine.Text = "Mangeek: não conectado";
                return;
            }
            var user = session.User;
            var name = string.Empty;
            if (user.HasValue && user.Value.ValueKind == System.Text.Json.JsonValueKind.Object &&
                user.Value.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String)
                name = n.GetString();
            _accountLine.Text = string.IsNullOrWhiteSpace(name) ? "Mangeek: conectado" : "Mangeek: " + name;
        }

        // ------------------------------------------------------------------
        // Navegação
        // ------------------------------------------------------------------
        public bool CanGoBack => _back.Count > 0;

        /// <summary>Guarda a página atual para o botão Voltar (Alt+Esquerda / mouse lateral).</summary>
        private void PushCurrent()
        {
            if (!(_host.Content is UIElement current)) return;
            _back.Add((current, _activeNav));
            if (_back.Count > 24) _back.RemoveAt(0);
        }

        /// <summary>Volta para a página anterior sem recriá-la (mantém rolagem e filtros).</summary>
        public bool GoBack()
        {
            if (_back.Count == 0) return false;
            var last = _back[_back.Count - 1];
            _back.RemoveAt(_back.Count - 1);
            if (last.Page is IPageSource ps) SyncSource(ps.PageSource);
            _activeNav = last.NavKey;
            SetActiveNav(last.NavKey);
            _pageImmersive = last.Page is ReaderPage;
            _host.Content = last.Page;
            SetImmersive(_fullscreen || _pageImmersive);
            (last.Page as IRefreshable)?.Refresh();
            FadeIn();
            return true;
        }

        public void Navigate(UIElement page, string navKey, bool resetStack = false)
        {
            if (resetStack) _back.Clear();
            else PushCurrent();
            SetActiveNav(navKey);
            _pageImmersive = false;
            SetImmersive(_fullscreen);
            _host.Content = page;
            FadeIn();
        }

        public void ShowImmersive(UIElement page)
        {
            PushCurrent();
            SetActiveNav(string.Empty);
            _pageImmersive = true;
            _host.Content = page;
            SetImmersive(true);
            FadeIn();
        }

        private void FadeIn()
        {
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            _host.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void SetImmersive(bool on)
        {
            _sidebar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            _sidebarCol.Width = on ? new GridLength(0) : new GridLength(252);
        }

        public void ShowHome()
        {
            var page = new HomePage(this);
            Navigate(page, "home", resetStack: true);
            _ = page.LoadAsync();
        }

        public void ShowCatalog(string q = null, string genre = null, bool resetStack = true, bool focus = false)
        {
            var page = new CatalogPage(this, q, genre);
            Navigate(page, "search", resetStack);
            if (focus)
                page.Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(page.FocusSearch), DispatcherPriority.Input);
            _ = page.LoadAsync();
        }

        public void ShowManga(Manga m) => ShowManga(m.Source, m.Id, m.Adult);

        public void ShowManga(string source, string id, bool adult, string openChapterId = null, bool resetStack = false)
        {
            SyncSource(source);
            var page = new MangaPage(this, source, id, adult, openChapterId);
            Navigate(page, string.Empty, resetStack);
            _ = page.LoadAsync();
        }

        public void ShowReader(Manga manga, List<Chapter> chapters, Chapter chapter)
        {
            var page = new ReaderPage(this, manga, chapters, chapter);
            ShowImmersive(page);
            _ = page.LoadAsync();
        }

        public void ShowFavorites()
        {
            var page = new FavoritesPage(this);
            Navigate(page, "favorites", resetStack: true);
        }

        public void ShowHistory()
        {
            var page = new HistoryPage(this);
            Navigate(page, "history", resetStack: true);
        }

        public void ShowSettings()
        {
            var page = new SettingsPage(this);
            Navigate(page, "settings", resetStack: true);
        }

        /// <summary>
        /// Verificação silenciosa de atualização ao abrir o app. Se houver versão
        /// nova, abre a janela de atualizações já com o manifesto em mãos. Falhas
        /// (sem internet, sem manifesto) não incomodam o usuário.
        /// </summary>
        private async Task CheckUpdatesOnStartupAsync()
        {
            Log.Write("update: verificacao no inicio (on=" + LocalStore.Instance.Settings.CheckUpdatesOnStartup + ")");
            if (!LocalStore.Instance.Settings.CheckUpdatesOnStartup) return;

            await Task.Delay(1800);

            if (UpdateWindow.AnyOpen)
            {
                Log.Write("update: janela ja aberta, pulando");
                return;
            }

            try
            {
                var info = await UpdateService.CheckAsync(
                    LocalStore.Instance.Settings.UpdateSource, CancellationToken.None);

                Log.Write("update: manifesto=" + info.Manifest + " versao=" + info.Parsed +
                    " instalada=" + UpdateService.CurrentVersion);

                if (info.Parsed > UpdateService.CurrentVersion && !UpdateWindow.AnyOpen)
                {
                    Log.Write("update: abrindo janela de atualizacao");
                    var window = new UpdateWindow(this, info);
                    window.ShowDialog();
                    Log.Write("update: janela fechada");
                }
            }
            catch (Exception ex)
            {
                // verificação automática é discreta: usuário pode checar manualmente
                Log.Write("update: falhou " + ex.Message);
            }
        }

        public void ShowLogin()
        {
            var page = new LoginPage(this);
            Navigate(page, "account", resetStack: true);
        }

        public void Toast(string message, bool error = false)
        {
            _toastText.Text = message;
            _toast.BorderBrush = error ? Theme.Accent2 : Theme.AccentLine;
            _toast.Background = error ? Theme.ToastError : Theme.Toast;
            _toast.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }
}
