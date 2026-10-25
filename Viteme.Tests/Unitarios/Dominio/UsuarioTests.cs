using Viteme.Domain.Entities;
using Viteme.Domain.Excecoes;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Unitarios.Dominio;

public class UsuarioTests
{
    [Fact]
    public void Quinta_falha_seguida_bloqueia_por_15_minutos()
    {
        var usuario = Dados.NovoUsuario();

        for (var i = 0; i < 4; i++)
        {
            usuario.RegistrarFalhaLogin(Dados.Agora);
            Assert.False(usuario.EstaBloqueado(Dados.Agora));
        }

        usuario.RegistrarFalhaLogin(Dados.Agora);

        Assert.True(usuario.EstaBloqueado(Dados.Agora));
        Assert.True(usuario.EstaBloqueado(Dados.Agora.AddMinutes(14)));
        Assert.False(usuario.EstaBloqueado(Dados.Agora.AddMinutes(15)));
    }

    [Fact]
    public void Depois_do_bloqueio_a_contagem_recomeca()
    {
        var usuario = Dados.NovoUsuario();
        for (var i = 0; i < 5; i++)
        {
            usuario.RegistrarFalhaLogin(Dados.Agora);
        }

        var depois = Dados.Agora.AddMinutes(16);
        usuario.RegistrarFalhaLogin(depois);

        Assert.False(usuario.EstaBloqueado(depois));
        Assert.Equal(1, usuario.TentativasLogin);
    }

    [Fact]
    public void Login_com_sucesso_zera_tentativas_e_bloqueio()
    {
        var usuario = Dados.NovoUsuario();
        usuario.RegistrarFalhaLogin(Dados.Agora);

        usuario.RegistrarLoginComSucesso();

        Assert.Equal(0, usuario.TentativasLogin);
        Assert.Null(usuario.BloqueadoAte);
    }

    [Fact]
    public void Trocar_telefone_desfaz_verificacao_e_aceite_de_sms()
    {
        var usuario = Dados.NovoUsuario(telefone: "+5544999990000");
        usuario.MarcarTelefoneVerificado();
        usuario.DefinirAceiteSms(true, Dados.Agora);
        Assert.True(usuario.PodeReceberSms);

        usuario.AlterarTelefone("+5544988880000");

        Assert.False(usuario.TelefoneVerificado);
        Assert.False(usuario.AceitaSms);
        Assert.Null(usuario.AceiteSmsEm);
        Assert.False(usuario.PodeReceberSms);
    }

    [Fact]
    public void Informar_o_mesmo_telefone_mantem_verificacao()
    {
        var usuario = Dados.NovoUsuario(telefone: "+5544999990000");
        usuario.MarcarTelefoneVerificado();

        usuario.AlterarTelefone("+5544999990000");

        Assert.True(usuario.TelefoneVerificado);
    }

    [Fact]
    public void Aceite_de_sms_exige_telefone_verificado()
    {
        var usuario = Dados.NovoUsuario(telefone: "+5544999990000");

        var erro = Assert.Throws<RegraDeNegocioException>(() => usuario.DefinirAceiteSms(true, Dados.Agora));

        Assert.Equal("TELEFONE_NAO_VERIFICADO", erro.Codigo);
    }

    [Fact]
    public void Aceite_de_sms_pode_ser_revogado()
    {
        var usuario = Dados.NovoUsuario(telefone: "+5544999990000");
        usuario.MarcarTelefoneVerificado();
        usuario.DefinirAceiteSms(true, Dados.Agora);

        usuario.DefinirAceiteSms(false, Dados.Agora);

        Assert.False(usuario.AceitaSms);
        Assert.Null(usuario.AceiteSmsEm);
    }
}
