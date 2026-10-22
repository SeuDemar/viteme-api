using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Domain.Entities;

/// <summary>Evento publicado no mural. Todo evento nasce como rascunho (RN06).</summary>
public class Evento
{
    private Evento()
    {
    }

    public Evento(
        Guid organizadorId,
        string titulo,
        string descricao,
        string categoria,
        string local,
        string? imagemUrl,
        DateTime dataInicio,
        DateTime dataFim,
        DateTime inscricaoInicio,
        DateTime inscricaoFim,
        int? capacidade,
        decimal preco,
        short? cargaHoraria)
    {
        ValidarPeriodos(dataInicio, dataFim, inscricaoInicio, inscricaoFim);

        Id = Guid.NewGuid();
        OrganizadorId = organizadorId;
        Status = StatusEvento.Rascunho;
        Titulo = titulo;
        Descricao = descricao;
        Categoria = categoria;
        Local = local;
        ImagemUrl = imagemUrl;
        DataInicio = dataInicio;
        DataFim = dataFim;
        InscricaoInicio = inscricaoInicio;
        InscricaoFim = inscricaoFim;
        Capacidade = capacidade;
        Preco = preco;
        CargaHoraria = cargaHoraria;
    }

    public Guid Id { get; private set; }

    public Guid OrganizadorId { get; private set; }

    public string Titulo { get; private set; } = null!;

    public string Descricao { get; private set; } = null!;

    public string Categoria { get; private set; } = null!;

    public string Local { get; private set; } = null!;

    public string? ImagemUrl { get; private set; }

    public DateTime DataInicio { get; private set; }

    public DateTime DataFim { get; private set; }

    public DateTime InscricaoInicio { get; private set; }

    public DateTime InscricaoFim { get; private set; }

    /// <summary>Nulo significa vagas ilimitadas (RN09).</summary>
    public int? Capacidade { get; private set; }

    public decimal Preco { get; private set; }

    public short? CargaHoraria { get; private set; }

    public StatusEvento Status { get; private set; }

    /// <summary>Data da republicação após ocultação (RN19).</summary>
    public DateTime? ReabertoEm { get; private set; }

    public DateTime CriadoEm { get; private set; }

    public DateTime AtualizadoEm { get; private set; }

    /// <summary>Coluna de sistema <c>xmin</c> do PostgreSQL, usada como ETag na concorrência otimista (RI11).</summary>
    public uint Versao { get; private set; }

    public ICollection<AtividadeProgramacao> Atividades { get; } = new List<AtividadeProgramacao>();

    public ICollection<ColaboradorEvento> Colaboradores { get; } = new List<ColaboradorEvento>();

    public bool Gratuito => Preco == 0;

    public bool Finalizado => Status is StatusEvento.Cancelado or StatusEvento.Encerrado;

    public bool AceitaInscricoes(DateTime agora) =>
        Status == StatusEvento.Publicado && agora >= InscricaoInicio && agora <= InscricaoFim;

    /// <summary>RN05: término após o início; inscrições terminam até o início do evento.</summary>
    public static void ValidarPeriodos(DateTime dataInicio, DateTime dataFim, DateTime inscricaoInicio, DateTime inscricaoFim)
    {
        if (dataFim <= dataInicio)
        {
            throw new RegraDeNegocioException("PERIODO_INVALIDO", "Período inválido",
                "A data de término deve ser posterior à data de início.", 400);
        }

        if (inscricaoFim <= inscricaoInicio || inscricaoFim > dataInicio)
        {
            throw new RegraDeNegocioException("PERIODO_INVALIDO", "Período inválido",
                "O período de inscrições deve terminar até o início do evento.", 400);
        }
    }

    public void AtualizarDados(
        string titulo,
        string descricao,
        string categoria,
        string local,
        string? imagemUrl,
        DateTime dataInicio,
        DateTime dataFim,
        DateTime inscricaoInicio,
        DateTime inscricaoFim,
        int? capacidade,
        decimal preco,
        short? cargaHoraria)
    {
        GarantirNaoFinalizado();
        ValidarPeriodos(dataInicio, dataFim, inscricaoInicio, inscricaoFim);

        Titulo = titulo;
        Descricao = descricao;
        Categoria = categoria;
        Local = local;
        ImagemUrl = imagemUrl;
        DataInicio = dataInicio;
        DataFim = dataFim;
        InscricaoInicio = inscricaoInicio;
        InscricaoFim = inscricaoFim;
        Capacidade = capacidade;
        Preco = preco;
        CargaHoraria = cargaHoraria;
    }

    /// <summary>RN06: publica um rascunho, ou republica um evento oculto (RN19).</summary>
    public void Publicar(DateTime agora)
    {
        GarantirNaoFinalizado();

        if (Status == StatusEvento.Publicado)
        {
            throw new RegraDeNegocioException("EVENTO_JA_PUBLICADO", "Evento já publicado",
                "O evento já está publicado.", 409);
        }

        if (string.IsNullOrWhiteSpace(Titulo) || string.IsNullOrWhiteSpace(Descricao)
            || string.IsNullOrWhiteSpace(Categoria) || string.IsNullOrWhiteSpace(Local))
        {
            throw new RegraDeNegocioException("DADOS_INCOMPLETOS", "Dados incompletos",
                "Preencha todos os campos obrigatórios antes de publicar.");
        }

        if (DataInicio <= agora)
        {
            throw new RegraDeNegocioException("DATA_INICIO_PASSADA", "Data de início no passado",
                "A data de início deve ser futura para publicar o evento.");
        }

        if (Status == StatusEvento.Oculto)
        {
            ReabertoEm = agora;
        }

        Status = StatusEvento.Publicado;
    }

    /// <summary>RN19: com 5 denúncias, o evento publicado sai do mural.</summary>
    public void Ocultar()
    {
        if (Status != StatusEvento.Publicado)
        {
            throw new RegraDeNegocioException("EVENTO_INDISPONIVEL", "Evento indisponível",
                "Somente um evento publicado pode ser ocultado.");
        }

        Status = StatusEvento.Oculto;
    }

    /// <summary>RN14: evento encerrado não pode ser cancelado.</summary>
    public void Cancelar()
    {
        if (Status == StatusEvento.Cancelado)
        {
            throw new RegraDeNegocioException("EVENTO_JA_CANCELADO", "Evento já cancelado",
                "O evento já foi cancelado.", 409);
        }

        if (Status == StatusEvento.Encerrado)
        {
            throw new RegraDeNegocioException("EVENTO_ENCERRADO", "Evento encerrado",
                "Um evento encerrado não pode ser cancelado.");
        }

        Status = StatusEvento.Cancelado;
    }

    /// <summary>RN22: um evento publicado passa a encerrado após a data de término.</summary>
    public bool Encerrar(DateTime agora)
    {
        if (Status != StatusEvento.Publicado || DataFim > agora)
        {
            return false;
        }

        Status = StatusEvento.Encerrado;
        return true;
    }

    private void GarantirNaoFinalizado()
    {
        if (Finalizado)
        {
            throw new RegraDeNegocioException("EVENTO_FINALIZADO", "Evento finalizado",
                "O evento foi cancelado ou encerrado e não pode mais ser alterado.", 409);
        }
    }
}
