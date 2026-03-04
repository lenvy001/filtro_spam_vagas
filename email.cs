using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace Emailcs
{
    /// <summary>
    /// Representa um e-mail lido via IMAP com os campos usados na classificação.
    /// </summary>
    public class Email
    {
        public string Remetente { get; set; } = string.Empty;
        public string Assunto { get; set; } = string.Empty;
        public string Corpo { get; set; } = string.Empty;
        public DateTime DataEnvio { get; set; }
        public uint ImapUid { get; set; }
    }

    /// <summary>
    /// Classifica e-mails em categorias com base em regras configuradas no arquivo <c>regras.json</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// var filtro = new FiltroEmail();
    /// var resultado = filtro.Classificar(email);
    /// Console.WriteLine(resultado.Classificacao);
    /// </code>
    /// </example>
    public class FiltroEmail
    {
        private readonly RegrasFiltro regras;

        public FiltroEmail()
        {
            regras = CarregarRegras();
        }

        /// <summary>
        /// Classifica um e-mail em <c>prioridade</c>, <c>candidatura</c>, <c>alerta de vaga</c> ou <c>outros</c>.
        /// </summary>
        /// <param name="email">E-mail a ser classificado.</param>
        /// <returns>Resultado completo com score, hits e classe final.</returns>
        public ResultadoFiltro Classificar(Email email)
        {
            var resultado = new ResultadoFiltro
            {
                PalavrasChaveRuido = regras.PalavrasChaveRuido,
                PalavrasChavePrioridade = regras.PalavrasChavePrioridade,
                PalavrasChaveCandidatura = regras.PalavrasChaveCandidatura
            };

            var remetente = NormalizarTexto(email.Remetente);
            var assunto = NormalizarTexto(email.Assunto);
            var corpo = NormalizarTexto(email.Corpo);

            var scoreRuido = CalcularScore(
                palavrasChave: regras.PalavrasChaveRuido,
                remetentesChave: regras.RemetentesRuido,
                remetente: remetente,
                assunto: assunto,
                corpo: corpo,
                pesos: regras.Pesos,
                palavrasEncontradas: out var palavrasRuidoEncontradas,
                remetentesEncontrados: out var remetentesRuidoEncontrados);

            var scorePrioridade = CalcularScore(
                palavrasChave: regras.PalavrasChavePrioridade,
                remetentesChave: regras.RemetentesPrioridade,
                remetente: remetente,
                assunto: assunto,
                corpo: corpo,
                pesos: regras.Pesos,
                palavrasEncontradas: out var palavrasPrioridadeEncontradas,
                remetentesEncontrados: out var remetentesPrioridadeEncontrados);

            var scoreCandidatura = CalcularScore(
                palavrasChave: regras.PalavrasChaveCandidatura,
                remetentesChave: regras.RemetentesCandidatura,
                remetente: remetente,
                assunto: assunto,
                corpo: corpo,
                pesos: regras.Pesos,
                palavrasEncontradas: out var palavrasCandidaturaEncontradas,
                remetentesEncontrados: out var remetentesCandidaturaEncontrados);

            resultado.ScoreRuido = scoreRuido;
            resultado.ScorePrioridade = scorePrioridade;
            resultado.ScoreCandidatura = scoreCandidatura;

            var pontuacoes = new Dictionary<string, double>
            {
                ["alerta de vaga"] = scoreRuido,
                ["prioridade"] = scorePrioridade,
                ["candidatura"] = scoreCandidatura
            };

            var ordemDesempate = new Dictionary<string, int>
            {
                ["prioridade"] = 0,
                ["candidatura"] = 1,
                ["alerta de vaga"] = 2
            };

            var melhor = pontuacoes
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => ordemDesempate[kv.Key])
                .First();

            var segundoMelhor = pontuacoes
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => ordemDesempate[kv.Key])
                .Skip(1)
                .First();

            if (melhor.Value < regras.LimiarClassificacao)
            {
                resultado.Classificacao = "outros";
            }
            else
            {
                var diferenca = melhor.Value - segundoMelhor.Value;

                if (diferenca < regras.DiferencaMinimaEntreClasses)
                {
                    var empatadas = pontuacoes
                        .Where(kv => (melhor.Value - kv.Value) < regras.DiferencaMinimaEntreClasses)
                        .OrderBy(kv => ordemDesempate[kv.Key])
                        .ToList();

                    resultado.Classificacao = empatadas.First().Key;
                }
                else
                {
                    resultado.Classificacao = melhor.Key;
                }

            }

            resultado.PalavrasEncontradasRuido = palavrasRuidoEncontradas;
            resultado.PalavrasEncontradasPrioridade = palavrasPrioridadeEncontradas;
            resultado.PalavrasEncontradasCandidatura = palavrasCandidaturaEncontradas;
            resultado.RemetentesEncontradosRuido = remetentesRuidoEncontrados;
            resultado.RemetentesEncontradosPrioridade = remetentesPrioridadeEncontrados;
            resultado.RemetentesEncontradosCandidatura = remetentesCandidaturaEncontrados;

            return resultado;
        }

        private static double CalcularScore(
            List<string> palavrasChave,
            List<string> remetentesChave,
            string remetente,
            string assunto,
            string corpo,
            PesosFiltro pesos,
            out List<string> palavrasEncontradas,
            out List<string> remetentesEncontrados)
        {
            var score = 0.0;
            palavrasEncontradas = new List<string>();
            remetentesEncontrados = new List<string>();

            foreach (var palavraOriginal in palavrasChave)
            {
                var palavra = NormalizarTexto(palavraOriginal);
                var encontrou = false;

                if (!string.IsNullOrWhiteSpace(assunto) && assunto.Contains(palavra, StringComparison.Ordinal))
                {
                    score += pesos.Assunto;
                    encontrou = true;
                }

                if (!string.IsNullOrWhiteSpace(corpo) && corpo.Contains(palavra, StringComparison.Ordinal))
                {
                    score += pesos.Corpo;
                    encontrou = true;
                }

                if (encontrou)
                {
                    palavrasEncontradas.Add(palavraOriginal);
                }
            }

            foreach (var remetenteOriginal in remetentesChave)
            {
                var r = NormalizarTexto(remetenteOriginal);
                if (!string.IsNullOrWhiteSpace(remetente) && remetente.Contains(r, StringComparison.Ordinal))
                {
                    score += pesos.Remetente;
                    remetentesEncontrados.Add(remetenteOriginal);
                }
            }

            return score;
        }

        private static string NormalizarTexto(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }

            var decomposed = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in decomposed)
            {
                var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
                if (categoria == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '@' || c == '.' || c == '_' || c == '-')
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(' ');
                }
            }

            var normalizado = sb.ToString().Normalize(NormalizationForm.FormC);
            return string.Join(' ', normalizado.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static RegrasFiltro CarregarRegras()
        {
            var caminhoBase = AppContext.BaseDirectory;
            var caminhoRegras = Path.Combine(caminhoBase, "regras.json");

            if (!File.Exists(caminhoRegras))
            {
                caminhoRegras = Path.Combine(Directory.GetCurrentDirectory(), "regras.json");
            }

            if (!File.Exists(caminhoRegras))
            {
                throw new FileNotFoundException("Arquivo regras.json não encontrado.", "regras.json");
            }

            var json = File.ReadAllText(caminhoRegras);
            var regrasArquivo = JsonSerializer.Deserialize<RegrasFiltro>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return regrasArquivo ?? new RegrasFiltro();
        }
    }

    /// <summary>
    /// Regras de classificação carregadas de configuração externa.
    /// </summary>
    public class RegrasFiltro
    {
        public List<string> PalavrasChaveRuido { get; set; } = new List<string>();
        public List<string> PalavrasChavePrioridade { get; set; } = new List<string>();
        public List<string> PalavrasChaveCandidatura { get; set; } = new List<string>();
        public List<string> RemetentesRuido { get; set; } = new List<string>();
        public List<string> RemetentesPrioridade { get; set; } = new List<string>();
        public List<string> RemetentesCandidatura { get; set; } = new List<string>();
        public double LimiarClassificacao { get; set; } = 2.5;
        public double DiferencaMinimaEntreClasses { get; set; } = 1.0;
        public PesosFiltro Pesos { get; set; } = new PesosFiltro();
    }

    /// <summary>
    /// Pesos aplicados no cálculo de score por campo do e-mail.
    /// </summary>
    public class PesosFiltro
    {
        public double Assunto { get; set; } = 2.5;
        public double Corpo { get; set; } = 1.2;
        public double Remetente { get; set; } = 2.0;
    }

    /// <summary>
    /// Resultado produzido pela classificação de um e-mail.
    /// </summary>
    public class ResultadoFiltro
    {
        public List<string> PalavrasChaveRuido { get; set; } = new List<string>();
        public List<string> PalavrasChavePrioridade { get; set; } = new List<string>();
        public List<string> PalavrasChaveCandidatura { get; set; } = new List<string>();
        public string Classificacao { get; set; } = string.Empty;
        public double ScoreRuido { get; set; }
        public double ScorePrioridade { get; set; }
        public double ScoreCandidatura { get; set; }
        public List<string> PalavrasEncontradasRuido { get; set; } = new List<string>();
        public List<string> PalavrasEncontradasPrioridade { get; set; } = new List<string>();
        public List<string> PalavrasEncontradasCandidatura { get; set; } = new List<string>();
        public List<string> RemetentesEncontradosRuido { get; set; } = new List<string>();
        public List<string> RemetentesEncontradosPrioridade { get; set; } = new List<string>();
        public List<string> RemetentesEncontradosCandidatura { get; set; } = new List<string>();
    }

    /// <summary>
    /// Operações de leitura IMAP e aplicação de marcador no servidor de e-mail.
    /// </summary>
    public static class LeitorImapMailKit
    {
        /// <summary>
        /// Lê os últimos e-mails da mailbox configurada nas variáveis de ambiente IMAP.
        /// </summary>
        /// <returns>Lista de e-mails com UID e metadados para classificação.</returns>
        public static List<Email> LerUltimosEmails()
        {
            var host = Environment.GetEnvironmentVariable("IMAP_HOST") ?? "imap.gmail.com";
            var porta = int.TryParse(Environment.GetEnvironmentVariable("IMAP_PORT"), out var p) ? p : 993;
            var usuario = Environment.GetEnvironmentVariable("IMAP_USER") ?? string.Empty;
            var senha = Environment.GetEnvironmentVariable("IMAP_PASSWORD") ?? string.Empty;
            var mailbox = Environment.GetEnvironmentVariable("IMAP_MAILBOX") ?? "INBOX";
            var limite = int.TryParse(Environment.GetEnvironmentVariable("IMAP_LIMIT"), out var l) ? l : 10;

            if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(senha))
            {
                throw new InvalidOperationException("Defina IMAP_USER e IMAP_PASSWORD para usar IMAP.");
            }

            using var client = new ImapClient();
            client.Connect(host, porta, SecureSocketOptions.SslOnConnect);
            client.Authenticate(usuario, senha);

            var inbox = client.GetFolder(mailbox);
            inbox.Open(FolderAccess.ReadOnly);

            var uids = inbox.Search(SearchQuery.All);
            var selecionados = uids.TakeLast(Math.Max(limite, 1));

            var emails = new List<Email>();
            foreach (var uid in selecionados)
            {
                var msg = inbox.GetMessage(uid);
                emails.Add(new Email
                {
                    Remetente = msg.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty,
                    Assunto = msg.Subject ?? string.Empty,
                    Corpo = msg.TextBody ?? msg.HtmlBody ?? string.Empty,
                    DataEnvio = msg.Date.LocalDateTime,
                    ImapUid = uid.Id
                });
            }

            client.Disconnect(true);
            return emails;
        }

        /// <summary>
        /// Aplica marcador no e-mail conforme a classificação informada.
        /// </summary>
        /// <param name="email">E-mail com UID válido no servidor IMAP.</param>
        /// <param name="classificacao">Classe calculada pelo filtro.</param>
        /// <returns><c>true</c> quando o marcador é aplicado; caso contrário <c>false</c>.</returns>
        public static bool AplicarMarcador(Email email, string classificacao)
        {
            if (email.ImapUid == 0)
            {
                return false;
            }

            if (classificacao.Equals("outros", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var host = Environment.GetEnvironmentVariable("IMAP_HOST") ?? "imap.gmail.com";
            var porta = int.TryParse(Environment.GetEnvironmentVariable("IMAP_PORT"), out var p) ? p : 993;
            var usuario = Environment.GetEnvironmentVariable("IMAP_USER") ?? string.Empty;
            var senha = Environment.GetEnvironmentVariable("IMAP_PASSWORD") ?? string.Empty;
            var mailbox = Environment.GetEnvironmentVariable("IMAP_MAILBOX") ?? "INBOX";
            var habilitarMarcador = (Environment.GetEnvironmentVariable("IMAP_APLICAR_MARCADOR") ?? "1") == "1";

            if (!habilitarMarcador)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(senha))
            {
                throw new InvalidOperationException("Defina IMAP_USER e IMAP_PASSWORD para aplicar marcador.");
            }

            var marcador = classificacao switch
            {
                "prioridade" => "entrevista",
                "candidatura" => "candidatura",
                "alerta de vaga" => "alerta",
                _ => "outros"
            };

            using var client = new ImapClient();
            client.Connect(host, porta, SecureSocketOptions.SslOnConnect);
            client.Authenticate(usuario, senha);

            var inbox = client.GetFolder(mailbox);
            inbox.Open(FolderAccess.ReadWrite);

            var uid = new UniqueId(email.ImapUid);

            if (inbox is ImapFolder gmailFolder && client.Capabilities.HasFlag(ImapCapabilities.GMailExt1))
            {
                gmailFolder.AddLabels(new[] { uid }, new[] { marcador }, true);
            }
            else
            {
                inbox.AddFlags(new[] { uid }, MessageFlags.None, new HashSet<string> { marcador }, true);
            }

            client.Disconnect(true);
            return true;
        }
    }
}
