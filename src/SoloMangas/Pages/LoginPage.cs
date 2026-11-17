using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoloMangasNative
{
    public sealed class LoginPage : UserControl
    {
        private readonly MainWindow _host;
        private readonly StackPanel _root;
        private readonly TextBox _input;
        private readonly Button _loginBtn;
        private readonly TextBlock _feedback;

        public LoginPage(MainWindow host)
        {
            _host = host;
            _root = new StackPanel { Margin = new Thickness(30, 24, 30, 44), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
            _root.Children.Add(Theme.H1("Conta"));
            _root.Children.Add(Theme.Label("A sessão do Mangeek é compartilhada com a versão 1.8.", 13, Theme.Muted));

            _root.Children.Add(Ui.Section("Status"));

            var statusBox = new Border
            {
                Background = Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18),
            };
            var statusPanel = new StackPanel();
            var session = SourceRegistry.Instance.Session;
            var connected = !string.IsNullOrEmpty(session.Token);
            var name = string.Empty;
            if (connected && session.User.HasValue &&
                session.User.Value.ValueKind == System.Text.Json.JsonValueKind.Object &&
                session.User.Value.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String)
                name = n.GetString();

            statusPanel.Children.Add(new TextBlock
            {
                Text = connected ? "Conectado ao Mangeek" : "Não conectado",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = connected ? Theme.Good : Theme.Text,
            });
            statusPanel.Children.Add(new TextBlock
            {
                Text = connected
                    ? (string.IsNullOrWhiteSpace(name) ? "Sessão ativa neste computador." : "Usuário: " + name)
                    : "Entre para acessar favoritos, histórico e progresso sincronizados do Mangeek.",
                FontSize = 12.5,
                Foreground = Theme.Muted,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });

            if (connected)
            {
                var logout = Theme.Btn("Sair da conta", "DangerButton");
                logout.HorizontalAlignment = HorizontalAlignment.Left;
                logout.Margin = new Thickness(0, 14, 0, 0);
                logout.Click += (_, _) =>
                {
                    session.Clear();
                    _host.UpdateAccount();
                    _host.Toast("Sessão encerrada.");
                    _host.ShowLogin();
                };
                statusPanel.Children.Add(logout);
            }
            statusBox.Child = statusPanel;
            _root.Children.Add(statusBox);

            _root.Children.Add(Ui.Section("Entrar"));

            var help = new TextBlock
            {
                Text = "Cole o token JWT, o link de retorno do Google (mangeek://oauth/google/...) ou o código 4/... do fluxo do Google. " +
                       "Você também pode abrir o login no navegador e colar o resultado aqui.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Muted,
                FontSize = 12.5,
                LineHeight = 19,
                Margin = new Thickness(0, 0, 0, 12),
            };
            _root.Children.Add(help);

            _input = new TextBox
            {
                Height = 118,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(12, 10, 12, 10),
            };
            _root.Children.Add(_input);

            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 10) };
            _loginBtn = Theme.Btn("Entrar", "PrimaryButton");
            _loginBtn.Margin = new Thickness(0, 0, 10, 8);
            _loginBtn.Click += async (_, _) => await DoLoginAsync();
            actions.Children.Add(_loginBtn);

            var google = Theme.Btn("Abrir login do Google ↗", "GhostButton");
            google.Margin = new Thickness(0, 0, 10, 8);
            google.Click += async (_, _) =>
            {
                google.IsEnabled = false;
                google.Content = "Abrindo…";
                try
                {
                    var url = await SourceRegistry.Instance.Mangeek.ConsentUrlAsync();
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    _host.Toast("Conclua o login no navegador e cole aqui o link de retorno.");
                }
                catch (Exception ex)
                {
                    _host.Toast(ex.Message, true);
                }
                finally
                {
                    google.IsEnabled = true;
                    google.Content = "Abrir login do Google ↗";
                }
            };
            actions.Children.Add(google);
            _root.Children.Add(actions);

            _feedback = new TextBlock
            {
                Foreground = Theme.Muted,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
            _root.Children.Add(_feedback);

            Content = Ui.Scroll(_root);
        }

        private async System.Threading.Tasks.Task DoLoginAsync()
        {
            var text = (_input.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                _host.Toast("Cole o token, o link ou o código do Google.", true);
                return;
            }
            _loginBtn.IsEnabled = false;
            _loginBtn.Content = "Validando…";
            _feedback.Text = string.Empty;
            try
            {
                var result = await SourceRegistry.Instance.Mangeek.LoginSmartAsync(text);
                _host.UpdateAccount();
                var warning = result != null && result.TryGetValue("warning", out var w) ? Convert.ToString(w) : null;
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    _feedback.Foreground = Theme.Accent2;
                    _feedback.Text = warning;
                }
                else
                {
                    _feedback.Foreground = Theme.Good;
                    _feedback.Text = "Sessão salva com sucesso.";
                }
                _host.Toast("Login concluído.");
                _host.ShowLogin();
            }
            catch (Exception ex)
            {
                _feedback.Foreground = Theme.Accent2;
                _feedback.Text = ex.Message;
            }
            finally
            {
                _loginBtn.IsEnabled = true;
                _loginBtn.Content = "Entrar";
            }
        }
    }
}
