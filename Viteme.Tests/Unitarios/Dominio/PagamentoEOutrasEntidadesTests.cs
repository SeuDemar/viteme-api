using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;
using Viteme.Infrastructure.Persistencia;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Unitarios.Dominio;

public class PagamentoEOutrasEntidadesTests
{
    [Fact]
    public void Pagamento_exige_valor_positivo()
    {
        Assert.Throws<RegraDeNegocioException>(() => new Pagamento(Guid.NewGuid(), 0m));
    }

    [Fact]
    public void Pagamento_pendente_pode_ser_aprovado_e_depois_estornado()
    {
        var pagamento = new Pagamento(Guid.NewGuid(), 50m);

        pagamento.Aprovar("PIX");
        pagamento.Estornar();

        Assert.Equal(StatusPagamento.Estornado, pagamento.Status);
        Assert.Equal("PIX", pagamento.Metodo);
    }

    [Fact]
    public void Aprovacao_tardia_de_pagamento_expirado_e_aceita()
    {
        var pagamento = new Pagamento(Guid.NewGuid(), 50m);
        pagamento.Expirar();

        pagamento.Aprovar(null);

        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
    }

    [Fact]
    public void Estornar_exige_pagamento_aprovado()
    {
        Assert.Throws<RegraDeNegocioException>(() => new Pagamento(Guid.NewGuid(), 50m).Estornar());
    }

    [Fact]
    public void Envio_sms_tenta_ate_4_vezes_e_depois_falha_definitivamente()
    {
        var envio = new EnvioSms(Guid.NewGuid(), "+5544999990000");

        for (var i = 0; i < 3; i++)
        {
            envio.RegistrarFalha("provedor indisponível");
            Assert.Equal(StatusSms.Pendente, envio.Status);
            Assert.True(envio.PodeReenviar);
        }

        envio.RegistrarFalha("provedor indisponível");

        Assert.Equal(StatusSms.Falhou, envio.Status);
        Assert.False(envio.PodeReenviar);
        Assert.Equal(4, envio.Tentativas);
    }

    [Fact]
    public void Envio_sms_com_sucesso_nao_pode_ser_reenviado()
    {
        var envio = new EnvioSms(Guid.NewGuid(), "+5544999990000");

        envio.RegistrarSucesso(Dados.Agora);

        Assert.Equal(StatusSms.Enviado, envio.Status);
        Assert.False(envio.PodeReenviar);
    }

    [Fact]
    public void Denuncia_com_motivo_outro_exige_descricao()
    {
        Assert.Throws<RegraDeNegocioException>(() => new Denuncia(Guid.NewGuid(), Guid.NewGuid(), MotivoDenuncia.Outro, null));
        _ = new Denuncia(Guid.NewGuid(), Guid.NewGuid(), MotivoDenuncia.Spam, null);
    }

    [Fact]
    public void Codigo_de_verificacao_expira_em_10_minutos_e_limita_tentativas()
    {
        var codigo = new CodigoVerificacao(Guid.NewGuid(), "hash", Dados.Agora);

        Assert.True(codigo.EstaValido(Dados.Agora.AddMinutes(9)));
        Assert.False(codigo.EstaValido(Dados.Agora.AddMinutes(10)));

        for (var i = 0; i < CodigoVerificacao.MaxTentativas; i++)
        {
            codigo.RegistrarTentativa();
        }

        Assert.True(codigo.TentativasEsgotadas);
        Assert.False(codigo.EstaValido(Dados.Agora));
    }

    [Fact]
    public void Token_de_renovacao_vale_7_dias_e_pode_ser_revogado()
    {
        var token = new TokenRenovacao(Guid.NewGuid(), "hash", Dados.Agora);

        Assert.True(token.EstaAtivo(Dados.Agora.AddDays(6)));
        Assert.False(token.EstaAtivo(Dados.Agora.AddDays(7)));

        token.Revogar(Dados.Agora);

        Assert.False(token.EstaAtivo(Dados.Agora));
        Assert.Equal(Dados.Agora, token.RevogadoEm);
    }

    [Theory]
    [InlineData("PendentePagamento", "PENDENTE_PAGAMENTO")]
    [InlineData("Rascunho", "RASCUNHO")]
    [InlineData("ConteudoImproprio", "CONTEUDO_IMPROPRIO")]
    public void Enums_do_postgres_usam_rotulos_em_maiusculas(string membro, string esperado)
    {
        Assert.Equal(esperado, new NomeEnumPostgres().TranslateMemberName(membro));
    }
}
