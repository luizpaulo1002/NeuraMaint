using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MQTTnet;
using MQTTnet.Client;
using ScottPlot;

namespace MonitoramentoBomba
{
    public class Alarme
    {
        public string Tipo { get; set; }
        public string Mensagem { get; set; }
        public string Hora { get; set; }
    }

    public partial class MainWindow : Window
    {
        private IMqttClient mqttClient;
        private DispatcherTimer updateTimer;
        private ObservableCollection<Alarme> alarmes;

        // Histórico para gráficos
        private const int MAX_PONTOS = 100;
        private List<double> historicoTempo = new List<double>();
        private List<double> historicoPressaoEnt = new List<double>();
        private List<double> historicoPressaoSai = new List<double>();
        private List<double> historicoTemperatura = new List<double>();
        private List<double> historicoVibracao = new List<double>();

        // Valores de calibração
        private double pressaoNormalEntrada = 3.0;
        private double pressaoNormalSaida = 6.0;
        private double vibracaoNormal = 1.0;
        private double vazaoNormal = 8.0;

        private WpfPlot plotPrincipal;
        private int contadorPontos = 0;

        public MainWindow()
        {
            InitializeComponent();

            alarmes = new ObservableCollection<Alarme>();
            AlarmesLista.ItemsSource = alarmes;

            ConfigurarGrafico();
            IniciarConexaoMQTT();

            // Timer para atualizar interface
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromSeconds(1);
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }

        private void ConfigurarGrafico()
        {
            plotPrincipal = new WpfPlot();
            PlotContainer.Children.Clear();
            PlotContainer.Children.Add(plotPrincipal);

            plotPrincipal.Plot.Title("Histórico de Variáveis");
            plotPrincipal.Plot.XLabel("Tempo (s)");
            plotPrincipal.Plot.YLabel("Valores");
            plotPrincipal.Plot.Legend(location: Alignment.UpperRight);

            // Configurar tema
            plotPrincipal.Plot.Style.Background(figure: Color.FromArgb(255, 255, 255, 255),
                                               data: Color.FromArgb(255, 245, 245, 245));
            plotPrincipal.Plot.Style.ColorGrids(Color.FromArgb(100, 200, 200, 200));

            plotPrincipal.Refresh();
        }

        private async void IniciarConexaoMQTT()
        {
            try
            {
                var factory = new MqttFactory();
                mqttClient = factory.CreateMqttClient();

                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer("broker.hivemq.com", 1883) // Altere para seu broker
                    .WithClientId("WPF_Dashboard_" + Guid.NewGuid())
                    .WithCleanSession()
                    .Build();

                mqttClient.ApplicationMessageReceivedAsync += ProcessarMensagemMQTT;

                await mqttClient.ConnectAsync(options);

                var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter("planta1/bomba1/dados")
                    .Build();

                await mqttClient.SubscribeAsync(subscribeOptions);

                Dispatcher.Invoke(() =>
                {
                    StatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                    StatusText.Text = "CONECTADO";
                });

                AdicionarAlarme("INFO", "Sistema conectado com sucesso!");
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                    StatusText.Text = "DESCONECTADO";
                });

                AdicionarAlarme("ERRO", $"Falha na conexão MQTT: {ex.Message}");
            }
        }

        private Task ProcessarMensagemMQTT(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                var dados = JsonSerializer.Deserialize<DadosBomba>(payload);

                if (dados != null)
                {
                    Dispatcher.Invoke(() => AtualizarInterface(dados));
                }
            }
            catch (Exception ex)
            {
                AdicionarAlarme("ERRO", $"Erro ao processar dados: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private void AtualizarInterface(DadosBomba dados)
        {
            // Atualizar valores
            PressaoEntradaValor.Text = dados.PressaoEntrada.ToString("F2");
            PressaoSaidaValor.Text = dados.PressaoSaida.ToString("F2");
            TemperaturaValor.Text = dados.TemperaturaFluido.ToString("F1");
            TemperaturaBar.Value = dados.TemperaturaFluido;
            VibracaoValor.Text = dados.VibracaoMag.ToString("F3");
            VazaoValor.Text = dados.Vazao.ToString("F1");
            NivelValor.Text = dados.Nivel.ToString("F0");
            NivelBar.Value = dados.Nivel;

            UltimaAtualizacao.Text = DateTime.Now.ToString("HH:mm:ss");

            // Adicionar ao histórico
            contadorPontos++;
            historicoTempo.Add(contadorPontos);
            historicoPressaoEnt.Add(dados.PressaoEntrada);
            historicoPressaoSai.Add(dados.PressaoSaida);
            historicoTemperatura.Add(dados.TemperaturaFluido);
            historicoVibracao.Add(dados.VibracaoMag * 10); // Escalar para visualização

            // Limitar tamanho do histórico
            if (historicoTempo.Count > MAX_PONTOS)
            {
                historicoTempo.RemoveAt(0);
                historicoPressaoEnt.RemoveAt(0);
                historicoPressaoSai.RemoveAt(0);
                historicoTemperatura.RemoveAt(0);
                historicoVibracao.RemoveAt(0);
            }

            // Atualizar gráfico
            AtualizarGrafico();

            // Verificar alarmes
            VerificarAlarmes(dados);
        }

        private void AtualizarGrafico()
        {
            plotPrincipal.Plot.Clear();

            if (historicoTempo.Count > 1)
            {
                var tempArray = historicoTempo.ToArray();

                plotPrincipal.Plot.AddScatter(tempArray, historicoPressaoEnt.ToArray(),
                    color: System.Drawing.Color.Blue, lineWidth: 2, label: "P. Entrada (bar)");

                plotPrincipal.Plot.AddScatter(tempArray, historicoPressaoSai.ToArray(),
                    color: System.Drawing.Color.Green, lineWidth: 2, label: "P. Saída (bar)");

                plotPrincipal.Plot.AddScatter(tempArray, historicoTemperatura.ToArray(),
                    color: System.Drawing.Color.Orange, lineWidth: 2, label: "Temperatura (°C)");

                plotPrincipal.Plot.AddScatter(tempArray, historicoVibracao.ToArray(),
                    color: System.Drawing.Color.Purple, lineWidth: 2, label: "Vibração (g×10)");
            }

            plotPrincipal.Plot.AxisAutoX();
            plotPrincipal.Plot.AxisAutoY();
            plotPrincipal.Refresh();
        }

        private void VerificarAlarmes(DadosBomba dados)
        {
            double pressaoDif = dados.PressaoSaida - dados.PressaoEntrada;

            // ALARME: Nível baixo
            if (dados.Nivel < 30 && dados.Nivel > 0)
            {
                AdicionarAlarme("CRÍTICO",
                    $"Nível do reservatório baixo: {dados.Nivel:F1}% - Risco de cavitação!");
            }

            // ALARME: Temperatura alta
            if (dados.TemperaturaFluido > 65)
            {
                AdicionarAlarme("ALERTA",
                    $"Temperatura elevada: {dados.TemperaturaFluido:F1}°C - Verificar refrigeração!");
            }

            // ALARME: Vibração alta
            if (dados.VibracaoMag > vibracaoNormal * 1.3)
            {
                AdicionarAlarme("ALERTA",
                    $"Vibração elevada: {dados.VibracaoMag:F2}g - Possível desalinhamento!");
            }

            // ALARME: Pressão diferencial baixa
            if (pressaoDif < 2.0)
            {
                AdicionarAlarme("ALERTA",
                    $"Pressão diferencial baixa: {pressaoDif:F2} bar - Possível vazamento interno!");
            }

            // ALARME: Vazão baixa
            if (dados.Vazao < vazaoNormal * 0.8 && dados.Vazao > 0)
            {
                AdicionarAlarme("ALERTA",
                    $"Vazão abaixo do normal: {dados.Vazao:F1} L/min - Verificar desgaste!");
            }

            // DIAGNÓSTICO COMBINADO: Cavitação
            if (dados.VibracaoMag > vibracaoNormal * 1.3 && dados.TemperaturaFluido > 60)
            {
                AdicionarAlarme("CRÍTICO",
                    "Cavitação detectada! Verificar nível do reservatório e filtros IMEDIATAMENTE!");
            }

            // DIAGNÓSTICO COMBINADO: Vazamento
            if (pressaoDif < 2.0 && dados.Vazao < vazaoNormal * 0.8)
            {
                AdicionarAlarme("CRÍTICO",
                    "Provável vazamento interno detectado! Verificar selos e vedações!");
            }
        }

        private void AdicionarAlarme(string tipo, string mensagem)
        {
            Dispatcher.Invoke(() =>
            {
                // Evitar alarmes duplicados recentes
                var ultimo = alarmes.FirstOrDefault();
                if (ultimo != null && ultimo.Mensagem == mensagem)
                {
                    var tempoUltimo = DateTime.Parse(ultimo.Hora);
                    if ((DateTime.Now - tempoUltimo).TotalSeconds < 60)
                        return;
                }

                alarmes.Insert(0, new Alarme
                {
                    Tipo = tipo,
                    Mensagem = mensagem,
                    Hora = DateTime.Now.ToString("HH:mm:ss")
                });

                // Manter apenas últimos 20 alarmes
                while (alarmes.Count > 20)
                {
                    alarmes.RemoveAt(alarmes.Count - 1);
                }
            });
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            // Verificar status da conexão
            if (mqttClient == null || !mqttClient.IsConnected)
            {
                StatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                StatusText.Text = "DESCONECTADO";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            updateTimer?.Stop();
            mqttClient?.DisconnectAsync();
            base.OnClosed(e);
        }
    }
}