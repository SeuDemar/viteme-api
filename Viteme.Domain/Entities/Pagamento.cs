using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Domain.Entities;

/// <summary>Cobrança gerada no gateway para uma inscrição. O valor é exatamente o preço do evento (RN12).</summary>
public class Pagamento
{
    private Pagamento()
    {
    }

    public Pagamento(Guid inscricaoId, decimal valor)
    {
        if (valor <= 0)
        {
            throw new RegraDeNegocioException("VALOR_INVALIDO", "Valor inválido",
                "O valor da cobrança deve ser maior que zero.", 400);
        }

        Id = Guid.NewGuid();
        InscricaoId = inscricaoId;
        Valor = valor;
        Status = StatusPagamento.Pendente;
    }

    public Guid Id { get; private set; }

    public Guid InscricaoId { get; private set; }

    public decimal Valor { get; private set; }

    public StatusPagamento Status { get; private set; }

    public string? Metodo { get; private set; }

    public string? IdTransacaoGateway { get; private set; }

    public string? UrlPagamento { get; private set; }

    public DateTime CriadoEm { get; private set; }

    public DateTime AtualizadoEm { get; private set; }

    /// <summary>Id da garantia (Conta Escrow) no gateway, quando a Fase 3 está ligada. O valor fica retido até a liberação (RN26).</summary>
    public string? GarantiaId { get; private set; }

    /// <summary>Quando a garantia foi encerrada e o valor liberado à subconta do organizador.</summary>
    public DateTime? GarantiaLiberadaEm { get; private set; }

    /// <summary>Coluna de sistema <c>xmin</c> (concorrência otimista entre webhook, expiração e estorno).</summary>
    public uint Versao { get; private set; }

    public void RegistrarCobranca(string idTransacaoGateway, string urlPagamento)
    {
        IdTransacaoGateway = idTransacaoGateway;
        UrlPagamento = urlPagamento;
    }

    /// <summary>Guarda o id da garantia devolvido pelo gateway na aprovação. Repetir o mesmo id (webhook reenviado) não faz nada.</summary>
    public void RegistrarGarantia(string garantiaId)
    {
        if (string.IsNullOrWhiteSpace(garantiaId))
        {
            throw new ArgumentException("O id da garantia é obrigatório.", nameof(garantiaId));
        }

        if (GarantiaId is not null && GarantiaId != garantiaId)
        {
            throw new RegraDeNegocioException("GARANTIA_JA_REGISTRADA", "Garantia já registrada",
                "Este pagamento já tem outra garantia registrada.", 409);
        }

        GarantiaId = garantiaId;
    }

    /// <summary>
    /// Pagamento aprovado sem garantia (recebido antes de a Conta Escrow ser ligada, ou sem subconta): o valor já está
    /// disponível, então ele sai da fila de liberação. Fica com <see cref="GarantiaLiberadaEm"/> preenchida e
    /// <see cref="GarantiaId"/> vazio.
    /// </summary>
    public void DispensarGarantia(DateTime agora)
    {
        if (GarantiaId is not null)
        {
            throw new RegraDeNegocioException("GARANTIA_JA_REGISTRADA", "Garantia já registrada",
                "Este pagamento tem garantia; ela precisa ser liberada, não dispensada.", 409);
        }

        GarantiaLiberadaEm ??= agora;
    }

    /// <summary>A garantia só é liberada para pagamento aprovado, uma vez (RN26). Estornado não libera: o valor volta ao participante.</summary>
    public void LiberarGarantia(DateTime agora)
    {
        if (GarantiaId is null)
        {
            throw new RegraDeNegocioException("PAGAMENTO_SEM_GARANTIA", "Pagamento sem garantia",
                "Este pagamento não tem garantia a liberar.", 409);
        }

        if (Status != StatusPagamento.Aprovado)
        {
            throw new RegraDeNegocioException("PAGAMENTO_NAO_APROVADO", "Pagamento não aprovado",
                "Somente a garantia de um pagamento aprovado pode ser liberada.", 409);
        }

        if (GarantiaLiberadaEm is not null)
        {
            throw new RegraDeNegocioException("GARANTIA_JA_LIBERADA", "Garantia já liberada",
                "A garantia deste pagamento já foi liberada.", 409);
        }

        GarantiaLiberadaEm = agora;
    }

    /// <summary>
    /// Aprovação tardia (pagamento já expirado ou recusado) também é registrada, pois o dinheiro entrou;
    /// o estorno é tratado pelo service (RN23).
    /// </summary>
    public void Aprovar(string? metodo)
    {
        if (Status is not (StatusPagamento.Pendente or StatusPagamento.Expirado or StatusPagamento.Recusado))
        {
            throw new RegraDeNegocioException("PAGAMENTO_NAO_PENDENTE", "Pagamento não pendente",
                "Somente um pagamento pendente pode ser aprovado.", 409);
        }

        Status = StatusPagamento.Aprovado;
        Metodo = metodo ?? Metodo;
    }

    public void Recusar()
    {
        if (Status != StatusPagamento.Pendente)
        {
            throw new RegraDeNegocioException("PAGAMENTO_NAO_PENDENTE", "Pagamento não pendente",
                "Somente um pagamento pendente pode ser recusado.", 409);
        }

        Status = StatusPagamento.Recusado;
    }

    public void Expirar()
    {
        if (Status != StatusPagamento.Pendente)
        {
            throw new RegraDeNegocioException("PAGAMENTO_NAO_PENDENTE", "Pagamento não pendente",
                "Somente um pagamento pendente pode expirar.", 409);
        }

        Status = StatusPagamento.Expirado;
    }

    public void Estornar()
    {
        if (Status != StatusPagamento.Aprovado)
        {
            throw new RegraDeNegocioException("PAGAMENTO_NAO_APROVADO", "Pagamento não aprovado",
                "Somente um pagamento aprovado pode ser estornado.", 409);
        }

        Status = StatusPagamento.Estornado;
    }
}
