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

    public void RegistrarCobranca(string idTransacaoGateway, string urlPagamento)
    {
        IdTransacaoGateway = idTransacaoGateway;
        UrlPagamento = urlPagamento;
    }

    /// <summary>Aprovação tardia (pagamento já expirado) também é registrada; o estorno é tratado pelo service (RN23).</summary>
    public void Aprovar(string? metodo)
    {
        if (Status is not (StatusPagamento.Pendente or StatusPagamento.Expirado))
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
