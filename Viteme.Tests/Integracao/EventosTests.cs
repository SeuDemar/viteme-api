using System.Net;
using Viteme.Application.Services;
using Viteme.Tests.Suporte;
using Microsoft.Extensions.DependencyInjection;

namespace Viteme.Tests.Integracao;

/// <summary>E11–E14: criação, detalhe, publicação, permissões por papel e encerramento. Casos CT14–CT22.</summary>
public class EventosTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private static async Task<string?> StatusAsync(HttpResponseMessage resposta) =>
        (await AmbienteApi.CorpoAsync(resposta)).GetProperty("status").GetString();

    [FactBanco]
    public async Task CT14_criacao_valida_gera_rascunho_e_o_criador_e_o_organizador()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoOrganizadorAsync();

        var resposta = await ambiente.PostAsync("/api/v1/eventos", ambiente.CorpoEvento(preco: 49.9m, capacidade: 30), organizador.Token);
        var corpo = await AmbienteApi.CorpoAsync(resposta);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal($"/api/v1/eventos/{corpo.GetProperty("id").GetGuid()}", resposta.Headers.Location!.OriginalString);
        Assert.NotNull(resposta.Headers.ETag);
        Assert.Equal("RASCUNHO", corpo.GetProperty("status").GetString());
        Assert.Equal(organizador.UsuarioId, corpo.GetProperty("organizador").GetProperty("id").GetGuid());
        Assert.True(corpo.GetProperty("souOrganizador").GetBoolean());
        Assert.Equal(49.9m, corpo.GetProperty("preco").GetDecimal());
        Assert.Equal(30, corpo.GetProperty("vagasRestantes").GetInt32());
    }

    [FactBanco]
    public async Task Criar_evento_exige_autenticacao()
    {
        using var ambiente = new AmbienteApi(banco, api);

        var resposta = await ambiente.PostAsync("/api/v1/eventos", ambiente.CorpoEvento());

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [FactBanco]
    public async Task CT15_termino_antes_do_inicio_retorna_400_com_erro_em_dataFim()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var inicio = AmbienteApi.Truncar(ambiente.Relogio.UtcAgora).AddDays(5);

        var resposta = await ambiente.PostAsync("/api/v1/eventos", new
        {
            titulo = "Evento inválido", descricao = "D", categoria = "c", local = "l",
            dataInicio = inicio, dataFim = inicio.AddHours(-1),
            inscricaoInicio = inicio.AddDays(-2), inscricaoFim = inicio.AddDays(-1), preco = 0,
        }, organizador.Token);
        var erros = (await AmbienteApi.CorpoAsync(resposta)).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.True(erros.TryGetProperty("dataFim", out _));
    }

    [FactBanco]
    public async Task CT16_inscricoes_depois_do_inicio_retorna_400_com_erro_em_inscricaoFim()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var inicio = AmbienteApi.Truncar(ambiente.Relogio.UtcAgora).AddDays(5);

        var resposta = await ambiente.PostAsync("/api/v1/eventos", new
        {
            titulo = "Evento inválido", descricao = "D", categoria = "c", local = "l",
            dataInicio = inicio, dataFim = inicio.AddHours(4),
            inscricaoInicio = inicio.AddDays(-2), inscricaoFim = inicio.AddMinutes(1), preco = 0,
        }, organizador.Token);
        var erros = (await AmbienteApi.CorpoAsync(resposta)).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.True(erros.TryGetProperty("inscricaoFim", out _));
    }

    [FactBanco]
    public async Task Criacao_valida_titulo_preco_capacidade_imagem_e_carga_horaria()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var inicio = AmbienteApi.Truncar(ambiente.Relogio.UtcAgora).AddDays(5);

        var resposta = await ambiente.PostAsync("/api/v1/eventos", new
        {
            titulo = "abc", descricao = "D", categoria = "c", local = "l",
            imagemUrl = "http://exemplo.com/a.png", capacidade = 0, cargaHoraria = 0, preco = 10.999m,
            dataInicio = inicio, dataFim = inicio.AddHours(4),
            inscricaoInicio = inicio.AddDays(-2), inscricaoFim = inicio.AddDays(-1),
        }, organizador.Token);
        var erros = (await AmbienteApi.CorpoAsync(resposta)).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        foreach (var campo in new[] { "titulo", "imagemUrl", "capacidade", "cargaHoraria", "preco" })
        {
            Assert.True(erros.TryGetProperty(campo, out _), $"esperava erro em {campo}");
        }
    }

    [FactBanco]
    public async Task CT17_publicacao_valida_coloca_o_evento_no_mural()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        var id = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Mural {marca}"));

        var publicacao = await ambiente.PostAsync($"/api/v1/eventos/{id}/publicar", token: organizador.Token);
        var mural = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos?busca={marca}"));

        Assert.Equal(HttpStatusCode.OK, publicacao.StatusCode);
        Assert.Equal("PUBLICADO", await StatusAsync(publicacao));
        Assert.Equal(1, mural.GetProperty("total").GetInt32());
        Assert.Equal(id, mural.GetProperty("itens")[0].GetProperty("id").GetGuid());
    }

    [FactBanco]
    public async Task CT18_publicar_com_data_de_inicio_no_passado_retorna_422()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromHours(1)));

        ambiente.Relogio.Avancar(TimeSpan.FromHours(2));
        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{id}/publicar", token: organizador.Token);

        Assert.Equal((HttpStatusCode)422, resposta.StatusCode);
        Assert.Equal("DATA_INICIO_PASSADA", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task Publicar_evento_ja_publicado_retorna_409()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);

        var resposta = await ambiente.PostAsync($"/api/v1/eventos/{id}/publicar", token: organizador.Token);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("EVENTO_JA_PUBLICADO", await AmbienteApi.CodigoErroAsync(resposta));
    }

    [FactBanco]
    public async Task CT19_rascunho_e_invisivel_para_quem_nao_e_organizador_nem_colaborador()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var outro = await ambiente.NovaSessaoAsync();
        var marca = Guid.NewGuid().ToString("N");
        var id = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento($"Rascunho {marca}"));

        var deOutro = await ambiente.GetAsync($"/api/v1/eventos/{id}", outro.Token);
        var anonimo = await ambiente.GetAsync($"/api/v1/eventos/{id}");
        var alteracoesAnonimo = await ambiente.GetAsync($"/api/v1/eventos/{id}/alteracoes");
        var mural = await AmbienteApi.CorpoAsync(await ambiente.GetAsync($"/api/v1/eventos?busca={marca}"));
        var doOrganizador = await ambiente.GetAsync($"/api/v1/eventos/{id}", organizador.Token);

        Assert.Equal(HttpStatusCode.NotFound, deOutro.StatusCode);
        Assert.Equal("EVENTO_NAO_ENCONTRADO", await AmbienteApi.CodigoErroAsync(deOutro));
        Assert.Equal(HttpStatusCode.NotFound, anonimo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, alteracoesAnonimo.StatusCode);
        Assert.Equal(0, mural.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, doOrganizador.StatusCode);
    }

    [FactBanco]
    public async Task Detalhe_de_evento_publicado_e_publico_e_traz_dados_do_usuario_logado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var participante = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(capacidade: 10), publicar: true);
        await ambiente.InscreverNoBancoAsync(id, participante.UsuarioId);
        await ambiente.InteresseNoBancoAsync(id, participante.UsuarioId);

        var anonimo = (await ambiente.DetalheAsync(id)).Corpo;
        var logado = (await ambiente.DetalheAsync(id, participante.Token)).Corpo;
        var doOrganizador = (await ambiente.DetalheAsync(id, organizador.Token)).Corpo;

        Assert.Equal(9, anonimo.GetProperty("vagasRestantes").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, anonimo.GetProperty("minhaInscricao").ValueKind);
        Assert.False(anonimo.GetProperty("interessado").GetBoolean());
        Assert.Equal("CONFIRMADA", logado.GetProperty("minhaInscricao").GetProperty("status").GetString());
        Assert.True(logado.GetProperty("interessado").GetBoolean());
        Assert.False(logado.GetProperty("souOrganizador").GetBoolean());
        Assert.True(doOrganizador.GetProperty("souOrganizador").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(anonimo.GetProperty("organizador").GetProperty("nome").GetString()));
    }

    [FactBanco]
    public async Task Rotas_de_organizador_respondem_401_403_ou_404_conforme_o_caso()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var estranho = await ambiente.NovaSessaoAsync();
        var rascunho = await ambiente.CriarEventoAsync(organizador);
        var publicado = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var corpo = new { motivo = "Motivo com mais de dez caracteres" };

        var semToken = await ambiente.PostAsync($"/api/v1/eventos/{publicado}/publicar");
        var estranhoNoPublicado = await ambiente.PostAsync($"/api/v1/eventos/{publicado}/cancelar", corpo, estranho.Token);
        var estranhoNoRascunho = await ambiente.PostAsync($"/api/v1/eventos/{rascunho}/cancelar", corpo, estranho.Token);
        var inexistente = await ambiente.PostAsync($"/api/v1/eventos/{Guid.NewGuid()}/cancelar", corpo, organizador.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, estranhoNoPublicado.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await AmbienteApi.CodigoErroAsync(estranhoNoPublicado));
        Assert.Equal(HttpStatusCode.NotFound, estranhoNoRascunho.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [FactBanco]
    public async Task CT20_colaborador_nao_pode_editar_o_evento()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var email = await ambiente.CadastrarAsync();
        var colaborador = await ambiente.LoginAsync(email);
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var adicionar = await ambiente.PostAsync($"/api/v1/eventos/{id}/colaboradores", new { email }, organizador.Token);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);

        var resposta = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("descricao", "Nova descrição")), colaborador.Token, etag);

        Assert.Equal(HttpStatusCode.Created, adicionar.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [FactBanco]
    public async Task CT21_duas_edicoes_com_a_mesma_versao_a_segunda_retorna_412()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);
        var corpo = AmbienteApi.CorpoDeEdicao(detalhe, ("descricao", "Descrição atualizada"));

        var primeira = await ambiente.PutAsync($"/api/v1/eventos/{id}", corpo, organizador.Token, etag);
        var segunda = await ambiente.PutAsync($"/api/v1/eventos/{id}", corpo, organizador.Token, etag);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.NotEqual(etag, primeira.Headers.ETag!.ToString());
        Assert.Equal((HttpStatusCode)412, segunda.StatusCode);
        Assert.Equal("VERSAO_DESATUALIZADA", await AmbienteApi.CodigoErroAsync(segunda));
    }

    [FactBanco]
    public async Task Edicao_com_o_novo_etag_funciona_em_sequencia()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var (detalhe, etag) = await ambiente.DetalheAsync(id, organizador.Token);

        var primeira = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("descricao", "Primeira")), organizador.Token, etag);
        var segunda = await ambiente.PutAsync($"/api/v1/eventos/{id}",
            AmbienteApi.CorpoDeEdicao(detalhe, ("descricao", "Segunda")), organizador.Token, primeira.Headers.ETag!.ToString());

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal("Segunda", (await AmbienteApi.CorpoAsync(segunda)).GetProperty("descricao").GetString());
    }

    [FactBanco]
    public async Task Edicao_sem_if_match_retorna_428_e_com_valor_invalido_retorna_400()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador);
        var (detalhe, _) = await ambiente.DetalheAsync(id, organizador.Token);
        var corpo = AmbienteApi.CorpoDeEdicao(detalhe, ("descricao", "Outra"));

        var semHeader = await ambiente.PutAsync($"/api/v1/eventos/{id}", corpo, organizador.Token);
        var invalido = await ambiente.PutAsync($"/api/v1/eventos/{id}", corpo, organizador.Token, "abc");

        Assert.Equal((HttpStatusCode)428, semHeader.StatusCode);
        Assert.Equal("VERSAO_OBRIGATORIA", await AmbienteApi.CodigoErroAsync(semHeader));
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }

    [FactBanco]
    public async Task CT22_evento_publicado_com_termino_vencido_e_encerrado_pela_tarefa()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var id = await ambiente.CriarEventoAsync(organizador, publicar: true);

        ambiente.Relogio.Avancar(TimeSpan.FromDays(11));
        int primeiraExecucao;
        int segundaExecucao;
        using (var escopo = ambiente.Servicos.CreateScope())
        {
            var tarefa = escopo.ServiceProvider.GetRequiredService<EncerramentoEventosService>();
            primeiraExecucao = await tarefa.EncerrarVencidosAsync();
            segundaExecucao = await tarefa.EncerrarVencidosAsync();
        }

        var detalhe = await ambiente.GetAsync($"/api/v1/eventos/{id}");

        Assert.True(primeiraExecucao >= 1);
        Assert.Equal(0, segundaExecucao);
        Assert.Equal("ENCERRADO", await StatusAsync(detalhe));
    }

    [FactBanco]
    public async Task Tarefa_nao_encerra_evento_que_ainda_nao_terminou_nem_rascunho()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var futuro = await ambiente.CriarEventoAsync(organizador, publicar: true);
        var rascunho = await ambiente.CriarEventoAsync(organizador);

        ambiente.Relogio.Avancar(TimeSpan.FromDays(3));
        using (var escopo = ambiente.Servicos.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<EncerramentoEventosService>().EncerrarVencidosAsync();
        }

        Assert.Equal("PUBLICADO", await StatusAsync(await ambiente.GetAsync($"/api/v1/eventos/{futuro}")));
        Assert.Equal("RASCUNHO", await StatusAsync(await ambiente.GetAsync($"/api/v1/eventos/{rascunho}", organizador.Token)));
    }
}
