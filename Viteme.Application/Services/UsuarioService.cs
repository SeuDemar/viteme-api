using Viteme.Application.DTOs.Usuarios;
using Viteme.Application.Excecoes;
using Viteme.Application.Interfaces;
using Viteme.Application.Validacoes;
using Viteme.Domain.Entities;
using Viteme.Domain.Excecoes;

namespace Viteme.Application.Services;

/// <summary>Perfil, verificação de telefone, consentimento de SMS e CNPJ do organizador (E05–E09, E40). Regras RN18 e RN24.</summary>
public class UsuarioService(
    IUsuarioRepository usuarios,
    ICodigoVerificacaoRepository codigos,
    IHashSeguro hashSeguro,
    IGeradorAleatorio aleatorio,
    IProvedorSms provedorSms,
    IConsultaCnpj consultaCnpj,
    IUnitOfWork unitOfWork,
    IRelogio relogio)
{
    /// <summary>Códigos por telefone na janela (anti-abuso de SMS contra terceiros).</summary>
    private const int LimiteTelefoneEnvios = 5;

    private static readonly TimeSpan LimiteTelefoneJanela = TimeSpan.FromHours(1);

    public async Task<PerfilResponse> ObterPerfilAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
        Mapear(await ObterAsync(usuarioId, cancellationToken));

    public async Task<PerfilResponse> AtualizarPerfilAsync(
        Guid usuarioId, AtualizarPerfilRequest requisicao, CancellationToken cancellationToken = default)
    {
        var usuario = await ObterAsync(usuarioId, cancellationToken);
        var telefone = string.IsNullOrWhiteSpace(requisicao.Telefone) ? null : requisicao.Telefone.Trim();
        var telefoneMudou = telefone != usuario.Telefone;

        usuario.AtualizarNome(requisicao.Nome.Trim());
        usuario.AlterarTelefone(telefone);

        if (telefoneMudou)
        {
            // Códigos emitidos para o telefone antigo deixam de valer.
            await codigos.InvalidarPendentesAsync(usuario.Id, cancellationToken);
        }

        await unitOfWork.SalvarAsync(cancellationToken);
        return Mapear(usuario);
    }

    /// <summary>E07: gera e envia por SMS um código de 6 dígitos, com intervalo mínimo de 60 s entre envios.</summary>
    public async Task<CodigoEnviadoResponse> EnviarCodigoTelefoneAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var usuario = await ObterAsync(usuarioId, cancellationToken);
        var agora = relogio.UtcAgora;

        if (usuario.Telefone is null)
        {
            throw new RegraDeNegocioException("TELEFONE_NAO_INFORMADO", "Telefone não informado",
                "Cadastre um telefone antes de pedir o código de verificação.");
        }

        if (usuario.TelefoneVerificado)
        {
            throw new RegraDeNegocioException("TELEFONE_JA_VERIFICADO", "Telefone já verificado",
                "O telefone já foi verificado.");
        }

        var ultimo = await codigos.ObterUltimoAsync(usuario.Id, cancellationToken);
        if (ultimo is not null && agora - ultimo.CriadoEm < CodigoVerificacao.IntervaloEntreEnvios)
        {
            throw new RegraDeNegocioException("AGUARDE_NOVO_CODIGO", "Aguarde para pedir novo código",
                "Aguarde 60 segundos entre os pedidos de código.", 429);
        }

        // Limite por número: impede usar a plataforma para encher de SMS o telefone de terceiros com várias contas.
        if (await codigos.ContarEnviosParaTelefoneAsync(usuario.Telefone, agora - LimiteTelefoneJanela, cancellationToken) >= LimiteTelefoneEnvios)
        {
            throw new RegraDeNegocioException("LIMITE_EXCEDIDO", "Limite de códigos para este telefone",
                "Este telefone já recebeu códigos demais na última hora. Tente novamente mais tarde.", 429);
        }

        await codigos.InvalidarPendentesAsync(usuario.Id, cancellationToken);

        var codigoPuro = aleatorio.GerarCodigoNumerico(6);
        var codigo = new CodigoVerificacao(usuario.Id, hashSeguro.Calcular(codigoPuro), agora);
        codigos.Adicionar(codigo);
        await unitOfWork.SalvarAsync(cancellationToken);

        try
        {
            await provedorSms.EnviarAsync(usuario.Telefone, $"Viteme: seu código de verificação é {codigoPuro}. Vale por 10 minutos.", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RegraDeNegocioException("SMS_INDISPONIVEL", "SMS indisponível",
                "Não foi possível enviar o SMS agora. Tente novamente em instantes.", 502);
        }

        return new CodigoEnviadoResponse(codigo.ExpiraEm);
    }

    /// <summary>E08: confirma o telefone. A 6.ª tentativa errada invalida o código (TENTATIVAS_EXCEDIDAS).</summary>
    public async Task<TelefoneVerificadoResponse> VerificarTelefoneAsync(
        Guid usuarioId, VerificarTelefoneRequest requisicao, CancellationToken cancellationToken = default)
    {
        var usuario = await ObterAsync(usuarioId, cancellationToken);
        var agora = relogio.UtcAgora;

        if (usuario.Telefone is null)
        {
            throw new RegraDeNegocioException("TELEFONE_NAO_INFORMADO", "Telefone não informado",
                "Cadastre um telefone antes de verificá-lo.");
        }

        var codigo = await codigos.ObterUltimoPendenteAsync(usuario.Id, cancellationToken)
            ?? throw CodigoInvalido();

        if (codigo.TentativasEsgotadas)
        {
            codigo.Invalidar();
            await unitOfWork.SalvarAsync(cancellationToken);
            throw new RegraDeNegocioException("TENTATIVAS_EXCEDIDAS", "Tentativas excedidas",
                "O número máximo de tentativas foi atingido. Peça um novo código.");
        }

        if (codigo.Expirado(agora))
        {
            throw new RegraDeNegocioException("CODIGO_EXPIRADO", "Código expirado",
                "O código expirou. Peça um novo código.");
        }

        if (!hashSeguro.Confere(requisicao.Codigo, codigo.CodigoHash))
        {
            codigo.RegistrarTentativa();
            await unitOfWork.SalvarAsync(cancellationToken);
            throw CodigoInvalido();
        }

        codigo.MarcarComoUtilizado();
        usuario.MarcarTelefoneVerificado();
        await unitOfWork.SalvarAsync(cancellationToken);
        return new TelefoneVerificadoResponse(true);
    }

    public async Task<PreferenciasSmsResponse> DefinirPreferenciasSmsAsync(
        Guid usuarioId, PreferenciasSmsRequest requisicao, CancellationToken cancellationToken = default)
    {
        var usuario = await ObterAsync(usuarioId, cancellationToken);

        usuario.DefinirAceiteSms(requisicao.AceitaSms ?? false, relogio.UtcAgora);
        await unitOfWork.SalvarAsync(cancellationToken);

        return new PreferenciasSmsResponse(usuario.AceitaSms, usuario.AceiteSmsEm);
    }

    /// <summary>
    /// E40 (RN24): confere os dígitos verificadores, que o CNPJ não é de outra conta e que está ATIVO na Receita.
    /// Só então libera o usuário para organizar eventos.
    /// </summary>
    public async Task<PerfilResponse> DefinirCnpjAsync(Guid usuarioId, DefinirCnpjRequest requisicao, CancellationToken cancellationToken = default)
    {
        var usuario = await ObterAsync(usuarioId, cancellationToken);
        var cnpj = CnpjAttribute.Normalizar(requisicao.Cnpj);

        if (usuario.Cnpj == cnpj && usuario.PodeOrganizar)
        {
            return Mapear(usuario);
        }

        if (await usuarios.CnpjEmUsoPorOutroAsync(cnpj, usuario.Id, cancellationToken))
        {
            throw CnpjJaCadastrado();
        }

        DadosCnpj? dados;
        try
        {
            dados = await consultaCnpj.ConsultarAsync(cnpj, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new RegraDeNegocioException("CONSULTA_CNPJ_INDISPONIVEL", "Consulta de CNPJ indisponível",
                "Não foi possível consultar o CNPJ na Receita agora. Tente novamente em instantes.", 502);
        }

        if (dados is null)
        {
            throw new RegraDeNegocioException("CNPJ_NAO_ENCONTRADO", "CNPJ não encontrado",
                "A Receita Federal não tem registro deste CNPJ. Confira o número informado.");
        }

        if (!dados.Ativo)
        {
            throw new RegraDeNegocioException("CNPJ_INATIVO", "CNPJ inativo",
                $"O CNPJ está com situação cadastral {dados.Situacao} na Receita. Só CNPJ ativo pode organizar eventos.");
        }

        usuario.DefinirCnpj(cnpj, dados.RazaoSocial, relogio.UtcAgora);
        try
        {
            await unitOfWork.SalvarAsync(cancellationToken);
        }
        catch (ViolacaoDeUnicidadeException)
        {
            // Outra conta gravou o mesmo CNPJ entre a checagem e o salvamento.
            throw CnpjJaCadastrado();
        }

        return Mapear(usuario);
    }

    private async Task<Usuario> ObterAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        await usuarios.ObterPorIdAsync(usuarioId, cancellationToken)
        ?? throw new RegraDeNegocioException("USUARIO_NAO_ENCONTRADO", "Usuário não encontrado",
            "O usuário autenticado não existe mais.", 401);

    private static PerfilResponse Mapear(Usuario u) =>
        new(u.Id, u.Nome, u.Email, u.Telefone, u.TelefoneVerificado, u.AceitaSms, u.AceiteSmsEm,
            u.Cnpj, u.RazaoSocial, u.CnpjVerificadoEm, u.PodeOrganizar);

    private static RegraDeNegocioException CnpjJaCadastrado() =>
        new("CNPJ_JA_CADASTRADO", "CNPJ já cadastrado", "Este CNPJ já está vinculado a outra conta.", 409);

    private static RegraDeNegocioException CodigoInvalido() =>
        new("CODIGO_INVALIDO", "Código inválido", "O código informado está incorreto.");
}
