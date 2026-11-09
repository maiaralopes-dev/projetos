using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PortalTarefas.Web.Observabilidade;

/// <summary>Fontes próprias de traces e métricas do Portal de Tarefas.</summary>
public sealed class Telemetria
{
    public const string Nome = "PortalTarefas";
    public static readonly ActivitySource Fonte = new(Nome);

    private readonly Counter<long> _tarefasCriadas;
    private readonly Counter<long> _tarefasConcluidas;
    private readonly Histogram<double> _duracaoCalendario;

    public Telemetria(IMeterFactory fabrica)
    {
        var meter = fabrica.Create(Nome);
        // Métrica de negócio: dimensão "prioridade" tem só 3 valores possíveis (cardinalidade controlada).
        _tarefasCriadas = meter.CreateCounter<long>("portal.tarefas.criadas", "{tarefa}", "Tarefas cadastradas");
        _tarefasConcluidas = meter.CreateCounter<long>("portal.tarefas.concluidas", "{tarefa}", "Tarefas concluídas");
        // Métrica técnica: duração das chamadas à dependência externa, por resultado (sucesso/falha/circuito_aberto/timeout).
        _duracaoCalendario = meter.CreateHistogram<double>("portal.dependencia.calendario.duracao", "ms",
            "Duração das chamadas ao serviço de calendário (inclui tentativas)");
    }

    public void TarefaCriada(string prioridade) => _tarefasCriadas.Add(1, new KeyValuePair<string, object?>("prioridade", prioridade));
    public void TarefaConcluida() => _tarefasConcluidas.Add(1);
    public void ChamadaCalendario(double ms, string resultado) =>
        _duracaoCalendario.Record(ms, new KeyValuePair<string, object?>("resultado", resultado));
}
