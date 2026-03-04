using Emailcs;

/// <summary>
/// Ponto de entrada da aplicação em modo contínuo para execução 24/7.
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        var intervaloSegundos = int.TryParse(Environment.GetEnvironmentVariable("WORKER_INTERVAL_SECONDS"), out var s)
            ? Math.Max(s, 5)
            : 60;

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Processador iniciado. Intervalo: {intervaloSegundos}s.");

        var filtro = new FiltroEmail();

        while (true)
        {
            try
            {
                var emails = LeitorImapMailKit.LerUltimosEmails();

                foreach (var email in emails)
                {
                    var resultado = filtro.Classificar(email);
                    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {email.Remetente} => {resultado.Classificacao} | A:{resultado.ScoreRuido:F1} P:{resultado.ScorePrioridade:F1} C:{resultado.ScoreCandidatura:F1}");

                    try
                    {
                        var aplicouMarcador = LeitorImapMailKit.AplicarMarcador(email, resultado.Classificacao);
                        if (aplicouMarcador)
                        {
                            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Marcador aplicado no UID {email.ImapUid}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Falha ao aplicar marcador no UID {email.ImapUid}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Falha no ciclo de processamento: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervaloSegundos));
        }
    }
}
