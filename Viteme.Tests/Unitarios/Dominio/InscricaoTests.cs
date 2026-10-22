using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Unitarios.Dominio;

public class InscricaoTests
{
    private static readonly DateTime InicioEvento = Dados.Agora.AddDays(10);
    private static readonly DateTime FimEvento = InicioEvento.AddHours(8);

    private static Inscricao Gratuita() => Inscricao.CriarGratuita(Guid.NewGuid(), Guid.NewGuid(), "ABC123DEF456");

    private static Inscricao Pendente() =>
        Inscricao.CriarPendentePagamento(Guid.NewGuid(), Guid.NewGuid(), "ABC123DEF456", Dados.Agora);

    [Fact]
    public void Inscricao_gratuita_nasce_confirmada_sem_reserva()
    {
        var inscricao = Gratuita();

        Assert.Equal(StatusInscricao.Confirmada, inscricao.Status);
        Assert.Null(inscricao.ReservadaAte);
    }

    [Fact]
    public void Inscricao_paga_nasce_pendente_com_reserva_de_30_minutos()
    {
        var inscricao = Pendente();

        Assert.Equal(StatusInscricao.PendentePagamento, inscricao.Status);
        Assert.Equal(Dados.Agora.AddMinutes(30), inscricao.ReservadaAte);
        Assert.False(inscricao.ReservaVencida(Dados.Agora.AddMinutes(29)));
        Assert.True(inscricao.ReservaVencida(Dados.Agora.AddMinutes(30)));
    }

    [Fact]
    public void Confirmar_pendente_limpa_a_reserva()
    {
        var inscricao = Pendente();

        inscricao.Confirmar();

        Assert.Equal(StatusInscricao.Confirmada, inscricao.Status);
        Assert.Null(inscricao.ReservadaAte);
        Assert.False(inscricao.ReservaVencida(Dados.Agora.AddHours(5)));
    }

    [Fact]
    public void Expirar_so_vale_para_inscricao_pendente()
    {
        Assert.Throws<RegraDeNegocioException>(() => Gratuita().Expirar());

        var pendente = Pendente();
        pendente.Expirar();

        Assert.Equal(StatusInscricao.Expirada, pendente.Status);
        Assert.False(pendente.Ativa);
    }

    [Fact]
    public void Cancelar_duas_vezes_falha_com_409()
    {
        var inscricao = Gratuita();
        inscricao.Cancelar();

        var erro = Assert.Throws<RegraDeNegocioException>(inscricao.Cancelar);

        Assert.Equal("INSCRICAO_JA_CANCELADA", erro.Codigo);
    }

    [Fact]
    public void Cancelamento_so_ate_24_horas_antes_do_inicio()
    {
        var inscricao = Gratuita();

        Assert.True(inscricao.DentroDoPrazoDeCancelamento(InicioEvento, InicioEvento.AddHours(-24)));
        Assert.False(inscricao.DentroDoPrazoDeCancelamento(InicioEvento, InicioEvento.AddHours(-3)));
    }

    [Fact]
    public void Checkin_dentro_da_janela_registra_presenca()
    {
        var inscricao = Gratuita();

        inscricao.RegistrarCheckin(InicioEvento, FimEvento, InicioEvento.AddHours(-2));

        Assert.True(inscricao.PresencaConfirmada);
        Assert.Equal(InicioEvento.AddHours(-2), inscricao.CheckinEm);
    }

    [Fact]
    public void Checkin_repetido_falha_com_409()
    {
        var inscricao = Gratuita();
        inscricao.RegistrarCheckin(InicioEvento, FimEvento, InicioEvento);

        var erro = Assert.Throws<RegraDeNegocioException>(() => inscricao.RegistrarCheckin(InicioEvento, FimEvento, InicioEvento));

        Assert.Equal("CHECKIN_JA_REALIZADO", erro.Codigo);
    }

    [Theory]
    [InlineData(-121)]
    [InlineData(8 * 60 + 1)]
    public void Checkin_fora_da_janela_falha(int minutosEmRelacaoAoInicio)
    {
        var inscricao = Gratuita();

        var erro = Assert.Throws<RegraDeNegocioException>(() =>
            inscricao.RegistrarCheckin(InicioEvento, FimEvento, InicioEvento.AddMinutes(minutosEmRelacaoAoInicio)));

        Assert.Equal("FORA_DO_HORARIO_DE_CHECKIN", erro.Codigo);
    }

    [Fact]
    public void Checkin_exige_inscricao_confirmada()
    {
        var erro = Assert.Throws<RegraDeNegocioException>(() => Pendente().RegistrarCheckin(InicioEvento, FimEvento, InicioEvento));

        Assert.Equal("INSCRICAO_NAO_CONFIRMADA", erro.Codigo);
    }
}
