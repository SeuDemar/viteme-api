using Microsoft.EntityFrameworkCore;
using Npgsql;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Infrastructure.Persistencia;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Integracao.Persistencia;

/// <summary>Verifica no PostgreSQL real o que a migration promete: chaves, restrições, enums, índices e concorrência.</summary>
public class PersistenciaTests(BancoFixture banco) : IClassFixture<BancoFixture>
{
    private static async Task<(Usuario Organizador, Evento Evento)> CriarEventoPublicadoAsync(VitemeDbContext contexto, decimal preco = 0)
    {
        var organizador = Dados.NovoUsuario();
        var evento = Dados.NovoEvento(organizador.Id, preco);
        evento.Publicar(Dados.Agora);
        contexto.AddRange(organizador, evento);
        await contexto.SaveChangesAsync();
        return (organizador, evento);
    }

    [FactBanco]
    public async Task Datas_automaticas_sao_preenchidas_em_utc_pelo_relogio()
    {
        var relogio = new RelogioFixo();
        await using var contexto = banco.CriarContexto(relogio);
        var usuario = Dados.NovoUsuario();
        contexto.Add(usuario);

        await contexto.SaveChangesAsync();
        relogio.Avancar(TimeSpan.FromHours(1));
        usuario.AtualizarNome("Novo Nome");
        await contexto.SaveChangesAsync();

        await using var leitura = banco.CriarContexto();
        var salvo = await leitura.Usuarios.SingleAsync(u => u.Id == usuario.Id);
        Assert.Equal(Dados.Agora, salvo.CriadoEm);
        Assert.Equal(Dados.Agora.AddHours(1), salvo.AtualizadoEm);
        Assert.Equal(DateTimeKind.Utc, salvo.CriadoEm.Kind);
    }

    [FactBanco]
    public async Task Email_duplicado_sem_diferenciar_maiusculas_e_rejeitado()
    {
        await using var contexto = banco.CriarContexto();
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        contexto.Add(Dados.NovoUsuario(email));
        await contexto.SaveChangesAsync();

        await using var outro = banco.CriarContexto();
        outro.Add(Dados.NovoUsuario(email.ToUpperInvariant()));

        var erro = await Assert.ThrowsAsync<DbUpdateException>(() => outro.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)erro.InnerException!).SqlState);
    }

    [FactBanco]
    public async Task Evento_persiste_com_enum_nativo_e_preco_com_duas_casas()
    {
        await using var contexto = banco.CriarContexto();
        var (_, evento) = await CriarEventoPublicadoAsync(contexto, preco: 49.9m);

        await using var leitura = banco.CriarContexto();
        var salvo = await leitura.Eventos.SingleAsync(e => e.Id == evento.Id);
        var rotulo = await leitura.Database
            .SqlQuery<string>($"SELECT status::text AS \"Value\" FROM evento WHERE id = {evento.Id}")
            .SingleAsync();

        Assert.Equal(StatusEvento.Publicado, salvo.Status);
        Assert.Equal(49.90m, salvo.Preco);
        Assert.Equal("PUBLICADO", rotulo);
    }

    [FactBanco]
    public async Task Edicao_concorrente_do_evento_falha_pelo_xmin()
    {
        await using var contexto = banco.CriarContexto();
        var (_, evento) = await CriarEventoPublicadoAsync(contexto);

        await using var a = banco.CriarContexto();
        await using var b = banco.CriarContexto();
        var eventoA = await a.Eventos.SingleAsync(e => e.Id == evento.Id);
        var eventoB = await b.Eventos.SingleAsync(e => e.Id == evento.Id);

        eventoA.Cancelar();
        await a.SaveChangesAsync();
        eventoB.Cancelar();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    [FactBanco]
    public async Task Apenas_uma_inscricao_ativa_por_usuario_em_cada_evento()
    {
        await using var contexto = banco.CriarContexto();
        var (_, evento) = await CriarEventoPublicadoAsync(contexto);
        var participante = Dados.NovoUsuario();
        contexto.Add(participante);
        contexto.Add(Inscricao.CriarGratuita(evento.Id, participante.Id, Codigo()));
        await contexto.SaveChangesAsync();

        await using var outro = banco.CriarContexto();
        outro.Add(Inscricao.CriarGratuita(evento.Id, participante.Id, Codigo()));

        var erro = await Assert.ThrowsAsync<DbUpdateException>(() => outro.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)erro.InnerException!).SqlState);
    }

    [FactBanco]
    public async Task Reinscricao_e_permitida_depois_de_cancelar()
    {
        await using var contexto = banco.CriarContexto();
        var (_, evento) = await CriarEventoPublicadoAsync(contexto);
        var participante = Dados.NovoUsuario();
        var primeira = Inscricao.CriarGratuita(evento.Id, participante.Id, Codigo());
        contexto.AddRange(participante, primeira);
        await contexto.SaveChangesAsync();

        primeira.Cancelar();
        contexto.Add(Inscricao.CriarGratuita(evento.Id, participante.Id, Codigo()));
        await contexto.SaveChangesAsync();

        Assert.Equal(2, await contexto.Inscricoes.CountAsync(i => i.UsuarioId == participante.Id));
    }

    [FactBanco]
    public async Task Pagamentos_sem_id_de_transacao_podem_coexistir_mas_o_id_e_unico()
    {
        await using var contexto = banco.CriarContexto();
        var (_, evento) = await CriarEventoPublicadoAsync(contexto, preco: 50m);
        var u1 = Dados.NovoUsuario();
        var u2 = Dados.NovoUsuario();
        var i1 = Inscricao.CriarPendentePagamento(evento.Id, u1.Id, Codigo(), Dados.Agora);
        var i2 = Inscricao.CriarPendentePagamento(evento.Id, u2.Id, Codigo(), Dados.Agora);
        contexto.AddRange(u1, u2, i1, i2, new Pagamento(i1.Id, 50m), new Pagamento(i2.Id, 50m));
        await contexto.SaveChangesAsync();

        var pagamentos = await contexto.Pagamentos.ToListAsync();
        pagamentos[^2].RegistrarCobranca("pay_123", "https://pagamento.exemplo/1");
        pagamentos[^1].RegistrarCobranca("pay_123", "https://pagamento.exemplo/2");

        var erro = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)erro.InnerException!).SqlState);
    }

    [FactBanco]
    public async Task Usuario_com_evento_nao_pode_ser_excluido_fisicamente()
    {
        await using var contexto = banco.CriarContexto();
        var (organizador, _) = await CriarEventoPublicadoAsync(contexto);

        var erro = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var outro = banco.CriarContexto();
            outro.Remove(await outro.Usuarios.SingleAsync(u => u.Id == organizador.Id));
            await outro.SaveChangesAsync();
        });

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)erro.InnerException!).SqlState);
    }

    [FactBanco]
    public async Task Excluir_evento_remove_atividades_e_colaboradores_em_cascata()
    {
        await using var contexto = banco.CriarContexto();
        var organizador = Dados.NovoUsuario();
        var colaborador = Dados.NovoUsuario();
        var evento = Dados.NovoEvento(organizador.Id);
        evento.Atividades.Add(new AtividadeProgramacao(evento.Id, "Abertura", null, null, evento.DataInicio, evento.DataInicio.AddHours(1)));
        evento.Colaboradores.Add(new ColaboradorEvento(evento.Id, colaborador.Id, Dados.Agora));
        contexto.AddRange(organizador, colaborador, evento);
        await contexto.SaveChangesAsync();

        await using var exclusao = banco.CriarContexto();
        exclusao.Remove(await exclusao.Eventos.SingleAsync(e => e.Id == evento.Id));
        await exclusao.SaveChangesAsync();

        await using var leitura = banco.CriarContexto();
        Assert.False(await leitura.Atividades.AnyAsync(a => a.EventoId == evento.Id));
        Assert.False(await leitura.Colaboradores.AnyAsync(c => c.EventoId == evento.Id));
    }

    [FactBanco]
    public async Task Notificacao_mantem_historico_quando_o_evento_e_excluido()
    {
        await using var contexto = banco.CriarContexto();
        var organizador = Dados.NovoUsuario();
        var evento = Dados.NovoEvento(organizador.Id);
        var notificacao = new Notificacao(organizador.Id, evento.Id, TipoNotificacao.Alteracao, "Alterado", "O local mudou.");
        contexto.AddRange(organizador, evento, notificacao);
        await contexto.SaveChangesAsync();

        await using var exclusao = banco.CriarContexto();
        exclusao.Remove(await exclusao.Eventos.SingleAsync(e => e.Id == evento.Id));
        await exclusao.SaveChangesAsync();

        await using var leitura = banco.CriarContexto();
        var salva = await leitura.Notificacoes.SingleAsync(n => n.Id == notificacao.Id);
        Assert.Null(salva.EventoId);
    }

    [FactBanco]
    public async Task Envio_de_sms_acompanha_a_notificacao_e_e_unico_por_notificacao()
    {
        await using var contexto = banco.CriarContexto();
        var usuario = Dados.NovoUsuario();
        var notificacao = new Notificacao(usuario.Id, null, TipoNotificacao.Lembrete, "Lembrete", "Amanhã tem evento.");
        contexto.AddRange(usuario, notificacao, new EnvioSms(notificacao.Id, "+5544999990000"));
        await contexto.SaveChangesAsync();

        await using var outro = banco.CriarContexto();
        outro.Add(new EnvioSms(notificacao.Id, "+5544999990000"));

        await Assert.ThrowsAsync<DbUpdateException>(() => outro.SaveChangesAsync());
    }

    [FactBanco]
    public async Task Denuncia_repetida_do_mesmo_usuario_e_rejeitada()
    {
        await using var contexto = banco.CriarContexto();
        var (_, evento) = await CriarEventoPublicadoAsync(contexto);
        var denunciante = Dados.NovoUsuario();
        contexto.AddRange(denunciante, new Denuncia(evento.Id, denunciante.Id, MotivoDenuncia.Spam, null));
        await contexto.SaveChangesAsync();

        await using var outro = banco.CriarContexto();
        outro.Add(new Denuncia(evento.Id, denunciante.Id, MotivoDenuncia.Golpe, null));

        await Assert.ThrowsAsync<DbUpdateException>(() => outro.SaveChangesAsync());
    }

    [FactBanco]
    public async Task Banco_rejeita_sms_ativo_sem_telefone_verificado()
    {
        await using var contexto = banco.CriarContexto();
        var usuario = Dados.NovoUsuario();
        contexto.Add(usuario);
        await contexto.SaveChangesAsync();

        var erro = await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlAsync(
            $"UPDATE usuario SET aceita_sms = true WHERE id = {usuario.Id}"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, erro.SqlState);
    }

    [FactBanco]
    public async Task Banco_rejeita_evento_com_termino_antes_do_inicio()
    {
        await using var contexto = banco.CriarContexto();
        var (_, evento) = await CriarEventoPublicadoAsync(contexto);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlAsync(
            $"UPDATE evento SET data_fim = data_inicio - interval '1 hour' WHERE id = {evento.Id}"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, erro.SqlState);
    }

    private static string Codigo() => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
}
