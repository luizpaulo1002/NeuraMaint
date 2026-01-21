using System;
using System.Data.SQLite;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;

namespace MonitoramentoBomba
{
    //MODELO DE DADOS
    public class DadosBomba
    {
        public long Timestamp { get; set; }
        public double PressaoEntrada { get; set; }
        public double PressaoSaida { get; set; }
        public double TemperaturaFluido { get; set; }
        public double VibracaoX { get; set; }
        public double VibracaoY { get; set; }
        public double VibracaoZ { get; set; }
        public double VibracaoMag { get; set; }
        public double Vazao { get; set; }
        public double Nivel { get; set; }
    }

    //CLASSE PRINCIPAL
    public class MonitorBomba
    {
        private IMqttClient mqttClient;
        private string connectionString = "Data Source=bomba_dados.db;Version=3;";
        private const string MQTT_BROKER = "broker.hivemq.com"; // Alterar para seu broker
        private const int MQTT_PORT = 1883;
        private const string TOPIC_DADOS = "planta1/bomba1/dados";

        //CONSTRUTOR
        public MonitorBomba()
        {
            CriarBancoDados();
        }

        //CRIAR BANCO DE DADOS
        private void CriarBancoDados()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS Medicoes (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DataHora DATETIME DEFAULT CURRENT_TIMESTAMP,
                        Timestamp INTEGER,
                        PressaoEntrada REAL,
                        PressaoSaida REAL,
                        PressaoDiferencial REAL,
                        TemperaturaFluido REAL,
                        VibracaoX REAL,
                        VibracaoY REAL,
                        VibracaoZ REAL,
                        VibracaoMag REAL,
                        Vazao REAL,
                        Nivel REAL
                    );
                ";

                using (var command = new SQLiteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }

                Console.WriteLine("✓ Banco de dados criado/verificado com sucesso!");
            }
        }

        //INSERIR DADOS NO BANCO
        private void InserirMedicao(DadosBomba dados)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                double pressaoDiferencial = dados.PressaoSaida - dados.PressaoEntrada;

                string insertQuery = @"
                    INSERT INTO Medicoes (
                        Timestamp, PressaoEntrada, PressaoSaida, PressaoDiferencial,
                        TemperaturaFluido, VibracaoX, VibracaoY, VibracaoZ, VibracaoMag,
                        Vazao, Nivel
                    ) VALUES (
                        @Timestamp, @PressaoEntrada, @PressaoSaida, @PressaoDiferencial,
                        @TemperaturaFluido, @VibracaoX, @VibracaoY, @VibracaoZ, @VibracaoMag,
                        @Vazao, @Nivel
                    );
                ";

                using (var command = new SQLiteCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@Timestamp", dados.Timestamp);
                    command.Parameters.AddWithValue("@PressaoEntrada", dados.PressaoEntrada);
                    command.Parameters.AddWithValue("@PressaoSaida", dados.PressaoSaida);
                    command.Parameters.AddWithValue("@PressaoDiferencial", pressaoDiferencial);
                    command.Parameters.AddWithValue("@TemperaturaFluido", dados.TemperaturaFluido);
                    command.Parameters.AddWithValue("@VibracaoX", dados.VibracaoX);
                    command.Parameters.AddWithValue("@VibracaoY", dados.VibracaoY);
                    command.Parameters.AddWithValue("@VibracaoZ", dados.VibracaoZ);
                    command.Parameters.AddWithValue("@VibracaoMag", dados.VibracaoMag);
                    command.Parameters.AddWithValue("@Vazao", dados.Vazao);
                    command.Parameters.AddWithValue("@Nivel", dados.Nivel);

                    command.ExecuteNonQuery();
                }
            }
        }

        //DIAGNÓSTICO BASEADO EM REGRAS 
        private void VerificarAlarmes(DadosBomba dados)
        {
            double pressaoDiferencial = dados.PressaoSaida - dados.PressaoEntrada;

            Console.WriteLine("\n==========VERIFICAÇÃO DE ALARMES ==========");

            // ALARME 1: Nível baixo
            if (dados.Nivel < 30)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("⚠️ ALARME: Nível baixo ({0:F1}%) - Risco de cavitação!", dados.Nivel);
                Console.ResetColor();
            }

            // ALARME 2: Temperatura alta
            if (dados.TemperaturaFluido > 65)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("⚠️ ALARME: Temperatura elevada ({0:F1}°C) - Verificar refrigeração!", dados.TemperaturaFluido);
                Console.ResetColor();
            }

            // ALARME 3: Vibração alta
            if (dados.VibracaoMag > 1.3) // Assumindo calibração normal em 1.0g
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️ ALERTA: Vibração elevada ({0:F2}g) - Possível desalinhamento ou cavitação", dados.VibracaoMag);
                Console.ResetColor();
            }

            // ALARME 4: Pressão diferencial baixa
            if (pressaoDiferencial < 2.0) // Assumindo normal ~3.0 bar
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️ ALERTA: Pressão diferencial baixa ({0:F2} bar) - Possível vazamento interno", pressaoDiferencial);
                Console.ResetColor();
            }

            // ALARME 5: Vazão baixa
            if (dados.Vazao < 5.0 && dados.Vazao > 0) // Assumindo normal ~8-10 L/min
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️ ALERTA: Vazão baixa ({0:F1} L/min) - Verificar filtros e desgaste", dados.Vazao);
                Console.ResetColor();
            }

            // DIAGNÓSTICO COMBINADO
            if (dados.VibracaoMag > 1.3 && dados.TemperaturaFluido > 60)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("🚨 DIAGNÓSTICO: Cavitação detectada! Verificar nível e filtros imediatamente!");
                Console.ResetColor();
            }

            if (pressaoDiferencial < 2.0 && dados.Vazao < 6.0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("🚨 DIAGNÓSTICO: Provável vazamento interno. Verificar selos e vedações!");
                Console.ResetColor();
            }

            Console.WriteLine("===========================================\n");
        }

        // CONECTAR AO BROKER MQTT
        public async Task ConectarMQTT()
        {
            var factory = new MqttFactory();
            mqttClient = factory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(MQTT_BROKER, MQTT_PORT)
                .WithClientId("BackendMonitor_" + Guid.NewGuid())
                .WithCleanSession()
                .Build();

            // Configurar handler de mensagens
            mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                try
                {
                    string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Mensagem recebida: {payload}");

                    // Deserializar JSON
                    var dados = JsonSerializer.Deserialize<DadosBomba>(payload);

                    if (dados != null)
                    {
                        // Salvar no banco
                        InserirMedicao(dados);

                        // Verificar alarmes
                        VerificarAlarmes(dados);

                        // Exibir resumo
                        Console.WriteLine($"✓ Dados salvos - P.Ent: {dados.PressaoEntrada:F2} bar | " +
                                        $"P.Saída: {dados.PressaoSaida:F2} bar | " +
                                        $"Temp: {dados.TemperaturaFluido:F1}°C | " +
                                        $"Vibração: {dados.VibracaoMag:F2}g");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Erro ao processar mensagem: {ex.Message}");
                }
            };

            // Conectar
            Console.WriteLine("Conectando ao broker MQTT...");
            await mqttClient.ConnectAsync(options);
            Console.WriteLine("✓ Conectado ao broker MQTT!");

            // Subscrever ao tópico
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(TOPIC_DADOS))
                .Build();

            await mqttClient.SubscribeAsync(subscribeOptions);
            Console.WriteLine($"✓ Inscrito no tópico: {TOPIC_DADOS}");
            Console.WriteLine("\nAguardando dados...\n");
        }

        //DESCONECTAR
        public async Task Desconectar()
        {
            if (mqttClient != null && mqttClient.IsConnected)
            {
                await mqttClient.DisconnectAsync();
                Console.WriteLine("Desconectado do broker MQTT");
            }
        }

        //CONSULTAR DADOS HISTÓRICOS
        public void GerarRelatorio(int ultimosMinutos = 60)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT 
                        COUNT(*) as TotalRegistros,
                        AVG(PressaoEntrada) as MediaPressaoEntrada,
                        AVG(PressaoSaida) as MediaPressaoSaida,
                        AVG(TemperaturaFluido) as MediaTemperatura,
                        MAX(VibracaoMag) as MaxVibracao,
                        AVG(Vazao) as MediaVazao,
                        MIN(Nivel) as MinNivel
                    FROM Medicoes
                    WHERE DataHora >= datetime('now', '-' || @minutos || ' minutes')
                ";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@minutos", ultimosMinutos);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            Console.WriteLine($"\n========== RELATÓRIO - Últimos {ultimosMinutos} minutos ==========");
                            Console.WriteLine($"Total de registros: {reader["TotalRegistros"]}");
                            Console.WriteLine($"Pressão Entrada (média): {reader["MediaPressaoEntrada"]:F2} bar");
                            Console.WriteLine($"Pressão Saída (média): {reader["MediaPressaoSaida"]:F2} bar");
                            Console.WriteLine($"Temperatura (média): {reader["MediaTemperatura"]:F1}°C");
                            Console.WriteLine($"Vibração (máxima): {reader["MaxVibracao"]:F2}g");
                            Console.WriteLine($"Vazão (média): {reader["MediaVazao"]:F1} L/min");
                            Console.WriteLine($"Nível (mínimo): {reader["MinNivel"]:F1}%");
                            Console.WriteLine("=====================================================\n");
                        }
                    }
                }
            }
        }
    }

    //PROGRAMA PRINCIPAL
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("  Sistema de Monitoramento de Bomba");


            var monitor = new MonitorBomba();

            try
            {
                await monitor.ConectarMQTT();

                Console.WriteLine("\nPressione ENTER para gerar relatório ou 'Q' para sair...\n");

                while (true)
                {
                    var key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.Q)
                    {
                        break;
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        monitor.GerarRelatorio(60);
                    }
                }

                await monitor.Desconectar();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro: {ex.Message}");
            }

            Console.WriteLine("\nPrograma encerrado.");
        }
    }
}