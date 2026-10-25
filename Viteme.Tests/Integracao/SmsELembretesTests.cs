using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viteme.Application.Services;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao;

/// <summary>Canal SMS das notificações (RN16, CT42–CT44) e lembretes de 24 horas (RN17, CT46).</summary>
public class SmsELembretesTests(BancoFixture banco, ApiFactory api) : IClassFixture<BancoFixture>, IClassFixture<ApiFactory>
{
    private static Task<int> EnviarSmsAsync(AmbienteApi ambiente) =>
        ambiente.ExecutarAsync(s => s.GetRequiredService<EnvioSmsService>().EnviarPendentesAsync());

    private static Task<int> LembretesAsync(AmbienteApi ambiente) =>
        ambiente.ExecutarAsync(s => s.GetRequiredService<LembretesService>().EnviarAsync());

    /// <summary>Evento publicado, com o participante informado inscrito (ou só acompanhando) e depois cancelado.</summary>
    private static async Task CancelarEventoComAsync(AmbienteApi ambiente, Sessao organizador, Sessao seguidor)
    {
        var evento = await ambiente.CriarEventoAsync(organizador, publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/interesse", token: seguidor.Token);
        (await ambiente.PostAsync($"/api/v1/eventos/{evento}/cancelar", new { motivo = "Sem local disponível" }, organizador.Token))
            .EnsureSuccessStatusCode();
    }

    [FactBanco]
    public async Task CT42_usuario_com_telefone_verificado_e_aceite_recebe_a_notificacao_por_sms()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var seguidor = await ambiente.NovaSessaoComSmsAsync();

        await CancelarEventoComAsync(ambiente, organizador, seguidor);
        var enviados = await EnviarSmsAsync(ambiente);

        Assert.Equal(1, enviados);
        var (telefone, mensagem) = Assert.Single(ambiente.Sms.Enviados);
        Assert.StartsWith("Viteme: Evento cancelado.", mensagem);
        Assert.True(mensagem.Length <= 320);
        await using var contexto = banco.CriarContexto();
        var envio = await contexto.EnviosSms.SingleAsync(e => e.Telefone == telefone);
        Assert.Equal(StatusSms.Enviado, envio.Status);
        Assert.Equal(1, envio.Tentativas);
        Assert.NotNull(envio.EnviadoEm);
        Assert.Equal(0, await EnviarSmsAsync(ambiente)); // nada novo no ciclo seguinte
    }

    [FactBanco]
    public async Task CT43_sem_telefone_verificado_ou_sem_aceite_so_o_sino_e_usado()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var semTelefone = await ambiente.NovaSessaoAsync();
        var semAceite = await ambiente.NovaSessaoAsync("+5544977770000");
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/codigo", token: semAceite.Token);
        await ambiente.PostAsync("/api/v1/usuarios/me/telefone/verificar", new { codigo = ambiente.Sms.UltimoCodigo() }, semAceite.Token);
        ambiente.Sms.Enviados.Clear();

        await CancelarEventoComAsync(ambiente, organizador, semTelefone);
        await CancelarEventoComAsync(ambiente, organizador, semAceite);
        var enviados = await EnviarSmsAsync(ambiente);

        Assert.Equal(0, enviados);
        Assert.Empty(ambiente.Sms.Enviados);
        await using var contexto = banco.CriarContexto();
        var ids = new[] { semTelefone.UsuarioId, semAceite.UsuarioId };
        Assert.Equal(2, await contexto.Notificacoes.CountAsync(n => ids.Contains(n.UsuarioId)));
        Assert.Equal(0, await contexto.EnviosSms.CountAsync(e => contexto.Notificacoes.Any(n => n.Id == e.NotificacaoId && ids.Contains(n.UsuarioId))));
    }

    [FactBanco]
    public async Task CT44_falha_do_provedor_gera_ate_3_novas_tentativas_e_depois_marca_falhou_sem_afetar_a_operacao()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var seguidor = await ambiente.NovaSessaoComSmsAsync();
        ambiente.Sms.Falhar = true;

        await CancelarEventoComAsync(ambiente, organizador, seguidor); // a operação conclui mesmo com o provedor fora do ar
        for (var ciclo = 0; ciclo < 6; ciclo++)
        {
            await EnviarSmsAsync(ambiente);
        }

        await using var contexto = banco.CriarContexto();
        var envio = await contexto.EnviosSms.SingleAsync(e => contexto.Notificacoes.Any(n => n.Id == e.NotificacaoId && n.UsuarioId == seguidor.UsuarioId));
        Assert.Equal(StatusSms.Falhou, envio.Status);
        Assert.Equal(EnvioSms.MaxTentativas, envio.Tentativas);
        Assert.False(string.IsNullOrWhiteSpace(envio.UltimoErro));
        Assert.Empty(ambiente.Sms.Enviados);
    }

    [FactBanco]
    public async Task Falha_temporaria_e_superada_numa_nova_tentativa()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var seguidor = await ambiente.NovaSessaoComSmsAsync();
        ambiente.Sms.Falhar = true;
        await CancelarEventoComAsync(ambiente, organizador, seguidor);
        await EnviarSmsAsync(ambiente);

        ambiente.Sms.Falhar = false;
        var enviados = await EnviarSmsAsync(ambiente);

        Assert.Equal(1, enviados);
        await using var contexto = banco.CriarContexto();
        var envio = await contexto.EnviosSms.SingleAsync(e => contexto.Notificacoes.Any(n => n.Id == e.NotificacaoId && n.UsuarioId == seguidor.UsuarioId));
        Assert.Equal(StatusSms.Enviado, envio.Status);
        Assert.Equal(2, envio.Tentativas);
    }

    [FactBanco]
    public async Task Aceite_revogado_antes_do_envio_descarta_o_sms()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var seguidor = await ambiente.NovaSessaoComSmsAsync();
        await CancelarEventoComAsync(ambiente, organizador, seguidor);

        await ambiente.PutAsync("/api/v1/usuarios/me/preferencias-sms", new { aceitaSms = false }, seguidor.Token);
        var enviados = await EnviarSmsAsync(ambiente);

        Assert.Equal(0, enviados);
        Assert.Empty(ambiente.Sms.Enviados);
        await using var contexto = banco.CriarContexto();
        var envio = await contexto.EnviosSms.SingleAsync(e => contexto.Notificacoes.Any(n => n.Id == e.NotificacaoId && n.UsuarioId == seguidor.UsuarioId));
        Assert.Equal(StatusSms.Falhou, envio.Status);
    }

    private async Task<int> LembretesDoUsuarioAsync(Guid usuarioId, Guid eventoId, string titulo)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.Notificacoes.CountAsync(n => n.UsuarioId == usuarioId && n.EventoId == eventoId && n.Tipo == TipoNotificacao.Lembrete && n.Titulo == titulo);
    }

    [FactBanco]
    public async Task CT46_inscrito_confirmado_recebe_lembrete_24h_antes_do_inicio_uma_unica_vez()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var inscrito = await ambiente.NovaSessaoAsync();
        var interessado = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromDays(3)), publicar: true);
        await ambiente.InscreverComSucessoAsync(inscrito, evento);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/interesse", token: interessado.Token);

        await LembretesAsync(ambiente);
        Assert.Equal(0, await LembretesDoUsuarioAsync(inscrito.UsuarioId, evento, LembretesService.TituloInicio)); // faltam 3 dias
        ambiente.Relogio.Avancar(TimeSpan.FromDays(2) + TimeSpan.FromHours(2)); // faltam ~22 h
        await LembretesAsync(ambiente);
        await LembretesAsync(ambiente); // repetir o ciclo não duplica

        Assert.Equal(1, await LembretesDoUsuarioAsync(inscrito.UsuarioId, evento, LembretesService.TituloInicio));
        Assert.Equal(0, await LembretesDoUsuarioAsync(interessado.UsuarioId, evento, LembretesService.TituloInicio)); // só confirmados
        Assert.Equal(0, await LembretesDoUsuarioAsync(inscrito.UsuarioId, evento, LembretesService.TituloFimInscricoes)); // já inscrito
        Assert.Equal(1, await LembretesDoUsuarioAsync(interessado.UsuarioId, evento, LembretesService.TituloFimInscricoes));
        Assert.Equal(0, await LembretesDoUsuarioAsync(organizador.UsuarioId, evento, LembretesService.TituloInicio));
    }

    [FactBanco]
    public async Task Lembrete_de_inicio_nao_vai_para_inscricao_pendente_nem_depois_do_inicio()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var pendente = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(preco: 20, capacidade: 5, inicioEm: TimeSpan.FromDays(3)), publicar: true);
        await ambiente.InscreverEmEventoPagoAsync(pendente, evento);

        ambiente.Relogio.Avancar(TimeSpan.FromDays(2) + TimeSpan.FromHours(2));
        await LembretesAsync(ambiente);
        ambiente.Relogio.Avancar(TimeSpan.FromDays(2)); // evento já começou
        await LembretesAsync(ambiente);

        Assert.Equal(0, await LembretesDoUsuarioAsync(pendente.UsuarioId, evento, LembretesService.TituloInicio));
    }

    [FactBanco]
    public async Task Interessado_sem_inscricao_recebe_lembrete_do_fim_das_inscricoes_e_inscrito_nao()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var interessado = await ambiente.NovaSessaoAsync();
        var inscrito = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromDays(10)), publicar: true);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/interesse", token: interessado.Token);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/interesse", token: inscrito.Token);
        await ambiente.InscreverComSucessoAsync(inscrito, evento);

        await LembretesAsync(ambiente);
        Assert.Equal(0, await LembretesDoUsuarioAsync(interessado.UsuarioId, evento, LembretesService.TituloFimInscricoes));
        ambiente.Relogio.Avancar(TimeSpan.FromDays(9) + TimeSpan.FromHours(2)); // inscrições terminam em ~23 h
        await LembretesAsync(ambiente);

        Assert.Equal(1, await LembretesDoUsuarioAsync(interessado.UsuarioId, evento, LembretesService.TituloFimInscricoes));
        Assert.Equal(0, await LembretesDoUsuarioAsync(inscrito.UsuarioId, evento, LembretesService.TituloFimInscricoes));
    }

    [FactBanco]
    public async Task Evento_nao_publicado_nao_gera_lembretes()
    {
        using var ambiente = new AmbienteApi(banco, api);
        var organizador = await ambiente.NovaSessaoAsync();
        var inscrito = await ambiente.NovaSessaoAsync();
        var evento = await ambiente.CriarEventoAsync(organizador, ambiente.CorpoEvento(inicioEm: TimeSpan.FromDays(3)), publicar: true);
        await ambiente.InscreverComSucessoAsync(inscrito, evento);
        await ambiente.PostAsync($"/api/v1/eventos/{evento}/cancelar", new { motivo = "Sem local disponível" }, organizador.Token);

        ambiente.Relogio.Avancar(TimeSpan.FromDays(2) + TimeSpan.FromHours(2));

        await LembretesAsync(ambiente);

        Assert.Equal(0, await LembretesDoUsuarioAsync(inscrito.UsuarioId, evento, LembretesService.TituloInicio));
    }
}
