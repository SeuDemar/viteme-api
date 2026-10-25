using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;
using Viteme.Tests.Suporte;

namespace Viteme.Tests.Unitarios.Dominio;

public class EventoTests
{
    [Fact]
    public void Evento_nasce_como_rascunho()
    {
        var evento = Dados.NovoEvento(Guid.NewGuid());

        Assert.Equal(StatusEvento.Rascunho, evento.Status);
        Assert.False(evento.AceitaInscricoes(Dados.Agora));
    }

    [Fact]
    public void Termino_antes_do_inicio_e_rejeitado()
    {
        var inicio = Dados.Agora.AddDays(5);

        var erro = Assert.Throws<RegraDeNegocioException>(() => new Evento(
            Guid.NewGuid(), "T", "D", "C", "L", null, inicio, inicio.AddHours(-1), Dados.Agora, inicio, null, 0, null));

        Assert.Equal(400, erro.StatusHttp);
    }

    [Fact]
    public void Inscricoes_depois_do_inicio_do_evento_sao_rejeitadas()
    {
        var inicio = Dados.Agora.AddDays(5);

        Assert.Throws<RegraDeNegocioException>(() => new Evento(
            Guid.NewGuid(), "T", "D", "C", "L", null, inicio, inicio.AddHours(2), Dados.Agora, inicio.AddMinutes(1), null, 0, null));
    }

    [Fact]
    public void Publicar_rascunho_com_data_futura_funciona_e_abre_inscricoes()
    {
        var evento = Dados.NovoEvento(Guid.NewGuid());

        evento.Publicar(Dados.Agora);

        Assert.Equal(StatusEvento.Publicado, evento.Status);
        Assert.True(evento.AceitaInscricoes(Dados.Agora));
        Assert.False(evento.AceitaInscricoes(evento.InscricaoFim.AddMinutes(1)));
    }

    [Fact]
    public void Publicar_com_data_de_inicio_passada_falha()
    {
        var evento = Dados.NovoEvento(Guid.NewGuid());

        var erro = Assert.Throws<RegraDeNegocioException>(() => evento.Publicar(evento.DataInicio.AddMinutes(1)));

        Assert.Equal("DATA_INICIO_PASSADA", erro.Codigo);
    }

    [Fact]
    public void Publicar_evento_ja_publicado_falha_com_409()
    {
        var evento = Dados.NovoEvento(Guid.NewGuid());
        evento.Publicar(Dados.Agora);

        var erro = Assert.Throws<RegraDeNegocioException>(() => evento.Publicar(Dados.Agora));

        Assert.Equal(409, erro.StatusHttp);
    }

    [Fact]
    public void Republicar_evento_oculto_registra_reaberto_em()
    {
        var evento = Dados.NovoEvento(Guid.NewGuid());
        evento.Publicar(Dados.Agora);
        evento.Ocultar();

        evento.Publicar(Dados.Agora.AddHours(1));

        Assert.Equal(StatusEvento.Publicado, evento.Status);
        Assert.Equal(Dados.Agora.AddHours(1), evento.ReabertoEm);
    }

    [Fact]
    public void Evento_cancelado_nao_pode_ser_publicado_nem_editado()
    {
        var evento = Dados.NovoEvento(Guid.NewGuid());
        evento.Cancelar();

        var publicar = Assert.Throws<RegraDeNegocioException>(() => evento.Publicar(Dados.Agora));
        var editar = Assert.Throws<RegraDeNegocioException>(() => evento.AtualizarDados(
            "T", "D", "C", "L", null, evento.DataInicio, evento.DataFim, evento.InscricaoInicio, evento.InscricaoFim, 5, 0, null));

        Assert.Equal("EVENTO_FINALIZADO", publicar.Codigo);
        Assert.Equal("EVENTO_FINALIZADO", editar.Codigo);
    }

    [Fact]
    public void Cancelar_duas_vezes_falha_com_409()
    {
        var evento = Dados.NovoEvento(Guid.NewGuid());
        evento.Cancelar();

        var erro = Assert.Throws<RegraDeNegocioException>(evento.Cancelar);

        Assert.Equal("EVENTO_JA_CANCELADO", erro.Codigo);
    }

    [Fact]
    public void Evento_encerrado_nao_pode_ser_cancelado()
    {
        var evento = Dados.NovoEvento(Guid.NewGuid());
        evento.Publicar(Dados.Agora);
        evento.Encerrar(evento.DataFim.AddMinutes(1));

        var erro = Assert.Throws<RegraDeNegocioException>(evento.Cancelar);

        Assert.Equal("EVENTO_ENCERRADO", erro.Codigo);
    }

    [Fact]
    public void Encerrar_so_acontece_depois_do_termino_de_evento_publicado()
    {
        var evento = Dados.NovoEvento(Guid.NewGuid());
        evento.Publicar(Dados.Agora);

        Assert.False(evento.Encerrar(evento.DataFim.AddMinutes(-1)));
        Assert.True(evento.Encerrar(evento.DataFim));
        Assert.Equal(StatusEvento.Encerrado, evento.Status);
    }

    [Fact]
    public void Ocultar_exige_evento_publicado()
    {
        var evento = Dados.NovoEvento(Guid.NewGuid());

        Assert.Throws<RegraDeNegocioException>(evento.Ocultar);
    }

    [Fact]
    public void Evento_gratuito_tem_preco_zero()
    {
        Assert.True(Dados.NovoEvento(Guid.NewGuid()).Gratuito);
        Assert.False(Dados.NovoEvento(Guid.NewGuid(), preco: 50m).Gratuito);
    }
}
