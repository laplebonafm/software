using System;
using System.Windows;
using System.Windows.Threading;
using VirtualStreamPlayer.Api;
using VirtualStreamPlayer.Config;
using VirtualStreamPlayer.Ipc;
using VirtualStreamPlayer.Logging;
using VirtualStreamPlayer.Streaming;

namespace VirtualStreamPlayer
{
    public partial class MainWindow : Window
    {
        private readonly AppConfig _config;
        private readonly PlaybackEngine _engine = new();
        private readonly NamedPipeAudioServer _pipeServer;
        private readonly LocalApiServer _apiServer;
        private readonly DispatcherTimer _uiTimer;

        public MainWindow()
        {
            InitializeComponent();

            _config = ConfigManager.Load();
            _engine.MaxReconnectDelaySeconds = _config.MaxReconnectDelaySeconds;
            _pipeServer = new NamedPipeAudioServer(_config.PipeName);
            _apiServer = new LocalApiServer(_config.ApiPort, _engine, _pipeServer);

            UrlTextBox.Text = _config.StreamUrl;
            PipePathText.Text = $@"Named pipe: \\.\pipe\{_config.PipeName}";
            ApiUrlText.Text = $"API local: http://127.0.0.1:{_config.ApiPort}/api/status";

            _engine.StateChanged += state => Dispatcher.Invoke(() => StateText.Text = $"Estado: {Traducir(state)}");
            _engine.FormatReady += format =>
            {
                _pipeServer.SetFormat(format);
                Dispatcher.Invoke(() => FormatText.Text = $"Formato: {format}");
            };
            _engine.PcmChunkReady += chunk => _pipeServer.Broadcast(chunk);
            _engine.TitleChanged += title => Dispatcher.Invoke(() => TitleText.Text = $"Reproduciendo: {title}");
            _engine.Log += line => Dispatcher.Invoke(() => AppendLog(line));
            Logger.LineWritten += line => Dispatcher.Invoke(() => AppendLog(line));

            _pipeServer.Start();
            _apiServer.Start();

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTimer.Tick += (_, _) =>
            {
                ClientsText.Text = $"Clientes conectados al pipe: {_pipeServer.ConnectedClientCount}";
                StationText.Text = $"Emisora: {_engine.StationName ?? "-"}";
            };
            _uiTimer.Start();

            Closing += (_, _) =>
            {
                _config.StreamUrl = UrlTextBox.Text;
                ConfigManager.Save(_config);
                _engine.Stop();
                _pipeServer.Stop();
                _apiServer.Stop();
            };

            if (_config.ConnectOnStartup && !string.IsNullOrWhiteSpace(_config.StreamUrl))
                _engine.Start(_config.StreamUrl);
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Ingresa una URL de streaming.", "VirtualStreamPlayer",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _engine.Start(url);
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
            StateText.Text = "Estado: detenido";
            FormatText.Text = "Formato: -";
            TitleText.Text = "Reproduciendo: -";
        }

        private void AppendLog(string line)
        {
            LogTextBox.AppendText(line + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }

        private static string Traducir(PlaybackState state) => state switch
        {
            PlaybackState.Stopped => "detenido",
            PlaybackState.Connecting => "conectando...",
            PlaybackState.Playing => "reproduciendo",
            PlaybackState.Reconnecting => "reconectando...",
            _ => state.ToString()
        };
    }
}
