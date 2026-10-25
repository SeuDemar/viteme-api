using Viteme.Application.DTOs.Certificados;
using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;
using Viteme.Domain.Enums;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>Certificado de participação (E38, E39). Regra RN21.</summary>
public class CertificadoService(
    IInscricaoRepository inscricoes,
    IEventoRepository eventos,
    IUsuarioRepository usuarios,
    ICertificadoRepository certificados,
    ICertificadoPdfGerador gerador,
    IGeradorAleatorio aleatorio,
    IUnitOfWork unitOfWork,
    IRelogio relogio)
{
    private const int TamanhoCodigo = 16;
    private const int Tentativas = 3;
    private const string RestricaoInscricao = "ix_certificado_inscricao_id";

    /// <summary>
    /// E38: emite o certificado na primeira chamada e devolve o mesmo (mesmo código) nas seguintes. RN21: inscrição
    /// confirmada com presença registrada, evento encerrado e com carga horária informada.
    /// </summary>
    public async Task<CertificadoArquivo> ObterOuEmitirAsync(Guid inscricaoId, Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var inscricao = await inscricoes.ObterPorIdAsync(inscricaoId, cancellationToken);
        if (inscricao is null || inscricao.UsuarioId != usuarioId)
        {
            throw PagamentoService.InscricaoNaoEncontrada();
        }

        var evento = await eventos.ObterPorIdAsync(inscricao.EventoId, cancellationToken) ?? throw PagamentoService.InscricaoNaoEncontrada();
        if (inscricao.Status != StatusInscricao.Confirmada || !inscricao.PresencaConfirmada
            || evento.Status != StatusEvento.Encerrado || evento.CargaHoraria is not { } cargaHoraria)
        {
            throw new RegraDeNegocioException("CERTIFICADO_INDISPONIVEL", "Certificado indisponível",
                "O certificado exige presença registrada, evento encerrado e carga horária informada.");
        }

        var certificado = await certificados.ObterPorInscricaoAsync(inscricaoId, cancellationToken)
            ?? await EmitirAsync(inscricaoId, cancellationToken);

        var participante = await usuarios.ObterPorIdAsync(usuarioId, cancellationToken);
        var pdf = gerador.Gerar(new DadosCertificado(
            participante?.Nome ?? string.Empty, evento.Titulo, evento.DataInicio, evento.DataFim,
            cargaHoraria, certificado.CodigoValidacao, certificado.EmitidoEm));

        return new CertificadoArquivo(pdf, $"certificado-{certificado.CodigoValidacao}.pdf");
    }

    /// <summary>E39: validação pública pelo código impresso no certificado.</summary>
    public async Task<ValidacaoCertificadoResponse> ValidarAsync(string codigo, CancellationToken cancellationToken = default)
    {
        var dados = await certificados.ObterValidacaoPorCodigoAsync(codigo.Trim().ToUpperInvariant(), cancellationToken)
            ?? throw new RegraDeNegocioException("CERTIFICADO_NAO_ENCONTRADO", "Certificado não encontrado",
                "Nenhum certificado tem este código.", 404);

        return new ValidacaoCertificadoResponse(true, dados.Participante, dados.Evento, dados.DataEvento, dados.CargaHoraria, dados.EmitidoEm);
    }

    private async Task<Certificado> EmitirAsync(Guid inscricaoId, CancellationToken cancellationToken)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            var certificado = new Certificado(inscricaoId, aleatorio.GerarCodigoAlfanumerico(TamanhoCodigo), relogio.UtcAgora);
            certificados.Adicionar(certificado);

            try
            {
                await unitOfWork.SalvarAsync(cancellationToken);
                return certificado;
            }
            catch (ViolacaoDeUnicidadeException ex) when (ex.Restricao == RestricaoInscricao)
            {
                // Duas chamadas simultâneas: a outra emitiu primeiro, e vale o certificado dela.
                unitOfWork.LimparRastreamento();
                var existente = await certificados.ObterPorInscricaoAsync(inscricaoId, cancellationToken);
                if (existente is null)
                {
                    throw;
                }

                return existente;
            }
            catch (ViolacaoDeUnicidadeException) when (tentativa < Tentativas)
            {
                // Colisão (improvável) do código de validação: gera outro.
                unitOfWork.LimparRastreamento();
            }
        }
    }
}
