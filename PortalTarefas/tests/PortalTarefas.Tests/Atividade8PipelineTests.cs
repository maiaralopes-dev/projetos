namespace PortalTarefas.Tests;

/// <summary>
/// Usado para demonstrar que uma falha de teste bloqueia a publicação:
/// com CI_FALHA_DELIBERADA=1 este teste falha de propósito.
/// </summary>
public class Atividade8PipelineTests
{
    [Fact]
    public void Gate_de_publicacao() =>
        Assert.False(Environment.GetEnvironmentVariable("CI_FALHA_DELIBERADA") == "1",
            "Falha deliberada para demonstrar o bloqueio da etapa de publicação.");
}
