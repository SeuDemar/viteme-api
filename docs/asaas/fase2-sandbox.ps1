#requires -Version 7
<#
.SYNOPSIS
  Fase 2/3 do plano Asaas (.claude/planning/pagamentos-asaas.md, seção 8.5): confere na conta sandbox PJ as chamadas que
  ContasRecebimentoAsaas.cs faz, e responde Q1, Q2, Q3, Q6 e "o escrow existe no sandbox?".

.DESCRIPTION
  Passos, na mesma ordem e com os mesmos corpos do adaptador:
    1. Cria uma subconta PJ de teste (POST /accounts) com dados fictícios e CNPJ de teste gerado (Q3).
    2. Com a chave da SUBCONTA: GET /myAccount/status (Q6) e GET /myAccount/documents (link de envio de documentos).
    3. Liga a Conta Escrow na subconta (POST /accounts/{id}/escrow) com a chave da raiz.
    4. Cobrança Pix com split de 100% para a carteira da subconta, paga pelo simulador do sandbox.
    5. Garantia da cobrança (GET /payments/{id}/escrow) com a chave da subconta e com a da raiz (Q1/Q2).
    6. Se houver garantia: encerra (POST /escrow/{id}/finish), primeiro com a chave da subconta, depois com a da raiz (Q1).

  Grava tudo em docs/evidencias/asaas-fase2-AAAA-MM-DD.json com as chaves mascaradas. A chave da subconta fica só em
  memória. Só roda contra o sandbox.

  A chave da conta raiz vem SÓ da variável de ambiente ASAAS_API_KEY.

.EXAMPLE
  ./docs/asaas/fase2-sandbox.ps1
#>
[CmdletBinding()]
param(
  [string] $UrlBase = 'https://api-sandbox.asaas.com/v3',
  [string] $Saida
)

$ErrorActionPreference = 'Stop'

if ($UrlBase -notmatch 'sandbox') { throw 'Este script só roda contra o sandbox do Asaas.' }
$chaveRaiz = $env:ASAAS_API_KEY
if ([string]::IsNullOrWhiteSpace($chaveRaiz)) { throw 'Defina ASAAS_API_KEY (chave do sandbox) antes de rodar.' }

$hoje = Get-Date -Format 'yyyy-MM-dd'
if (-not $Saida) { $Saida = Join-Path $PSScriptRoot "..\evidencias\asaas-fase2-$hoje.json" }

$script:segredos = @($chaveRaiz)
$evidencias = [ordered]@{ executadoEm = (Get-Date).ToString('o'); urlBase = $UrlBase; passos = @(); respostas = [ordered]@{} }

function Mascarar([string] $texto) {
  foreach ($s in $script:segredos) { if ($s) { $texto = $texto.Replace($s, '***') } }
  return [regex]::Replace($texto, '"apiKey"\s*:\s*"[^"]*"', '"apiKey":"***"')
}

function Gravar {
  $json = $evidencias | ConvertTo-Json -Depth 20
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Saida) | Out-Null
  Set-Content -Path $Saida -Value (Mascarar $json) -Encoding UTF8
}

function Chamar([string] $nome, [string] $metodo, [string] $rota, $corpo = $null, [string] $chave = $chaveRaiz, [switch] $EsperaErro) {
  $cabecalhos = @{ 'access_token' = $chave; 'User-Agent' = 'Viteme-Fase2/1.0'; 'Content-Type' = 'application/json' }
  $params = @{ Uri = "$UrlBase/$rota"; Method = $metodo; Headers = $cabecalhos; SkipHttpErrorCheck = $true; StatusCodeVariable = 'codigo' }
  if ($null -ne $corpo) { $params.Body = ($corpo | ConvertTo-Json -Depth 10 -Compress) }
  $resposta = Invoke-RestMethod @params
  $quem = if ($chave -eq $chaveRaiz) { 'raiz' } else { 'subconta' }
  $script:evidencias.passos += [ordered]@{ passo = $nome; requisicao = "$metodo /$rota"; chave = $quem; status = $codigo; resposta = $resposta }
  Write-Host ("[{0}] {1} ({2}) -> HTTP {3}" -f $nome, "$metodo /$rota", $quem, $codigo)
  if ($codigo -ge 400 -and -not $EsperaErro) {
    Gravar
    throw "Passo '$nome' falhou (HTTP $codigo). Evidências parciais gravadas em $Saida."
  }
  return @{ Status = $codigo; Corpo = $resposta }
}

# CNPJ de teste com dígitos verificadores válidos (o sandbox não pode repetir o de outra subconta).
function NovoCnpj {
  $base = @(1..8 | ForEach-Object { Get-Random -Minimum 0 -Maximum 10 }) + @(0, 0, 0, 1)
  foreach ($pesos in @(@(5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2), @(6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2))) {
    $soma = 0; for ($i = 0; $i -lt $pesos.Count; $i++) { $soma += $base[$i] * $pesos[$i] }
    $resto = $soma % 11
    $base += $(if ($resto -lt 2) { 0 } else { 11 - $resto })
  }
  return -join $base
}

$null = Chamar 'saldo-raiz' 'GET' 'finance/balance'

# 1. Q3: subconta PJ com os campos que o adaptador envia (ContasRecebimentoAsaas.CriarSubcontaAsync).
$cnpj = NovoCnpj
$sufixo = (Get-Date -Format 'HHmmss')
# Nome só com letras: números ou caracteres especiais no nome impedem a aprovação automática do sandbox e o Pix.
$letras = -join ((1..6) | ForEach-Object { [char](Get-Random -Minimum 97 -Maximum 123) })
$criacao = Chamar 'Q3-criar-subconta' 'POST' 'accounts' @{
  name = "Organizador Teste $letras"; email = "organizador-fase2-$hoje-$sufixo@exemplo.com"; cpfCnpj = $cnpj
  companyType = 'ASSOCIATION'; mobilePhone = '44999990000'; incomeValue = 5000
  postalCode = '87020900'; address = 'Av. Colombo'; addressNumber = '5790'; complement = $null; province = 'Zona 7'
} -EsperaErro
$evidencias.respostas['Q3_subcontaCriada'] = $criacao.Status -lt 400
if ($criacao.Status -ge 400) {
  Gravar
  Write-Warning 'A subconta não foi criada: a resposta lista os campos que faltam (Q3). Evidência gravada.'
  return
}

$sub = $criacao.Corpo
$chaveSub = $sub.apiKey
$script:segredos += $chaveSub
$evidencias.respostas['Q3_devolveIdWalletEApiKey'] = [bool]($sub.id -and $sub.walletId -and $chaveSub)

# 1b. Q6: aprovação simulada do sandbox (POST /sandbox/myAccount/approve, com a chave da própria subconta).
$aprovacao = Chamar 'Q6-aprovar-no-sandbox' 'POST' 'sandbox/myAccount/approve' @{} -chave $chaveSub -EsperaErro
$evidencias.respostas['Q6_situacaoAposAprovar'] = $aprovacao.Corpo
$evidencias.respostas['Q6_aprovacaoSandboxHttp'] = $aprovacao.Status
Start-Sleep -Seconds 3

# 2. Q6: situação e documentos, com a chave da subconta.
$situacao = Chamar 'Q6-situacao-subconta' 'GET' 'myAccount/status' -chave $chaveSub -EsperaErro
$evidencias.respostas['Q6_situacaoGeral'] = $situacao.Corpo.general
$evidencias.respostas['Q6_idDaSituacaoIgualAoDaConta'] = ($situacao.Corpo.id -eq $sub.id)
$documentos = Chamar 'Q6-documentos-subconta' 'GET' 'myAccount/documents' -chave $chaveSub -EsperaErro
$evidencias.respostas['Q6_temOnboardingUrl'] = [bool]($documentos.Corpo.data | Where-Object { $_.onboardingUrl } | Select-Object -First 1)

# 3. Escrow: existe no sandbox? (POST /accounts/{id}/escrow com a chave da raiz)
$escrow = Chamar 'escrow-ligar-na-subconta' 'POST' "accounts/$($sub.id)/escrow" @{ enabled = $true; daysToExpire = 30; isFeePayer = $false } -EsperaErro
$evidencias.respostas['escrow_ligadoPelaApi'] = $escrow.Status -lt 400
$null = Chamar 'escrow-consultar-subconta' 'GET' "accounts/$($sub.id)/escrow" -EsperaErro

# 4. Cobrança com split de 100% para a subconta, paga no simulador.
$cliente = Chamar 'cliente' 'POST' 'customers' @{
  name = 'Pagador Fase 2'; email = 'fase2@exemplo.com'; cpfCnpj = '12345678909'; externalReference = "fase2-$hoje"; notificationDisabled = $true
}
$cobranca = Chamar 'cobranca-com-split' 'POST' 'payments' @{
  customer = $cliente.Corpo.id; billingType = 'PIX'; value = 10.00
  dueDate = (Get-Date).AddDays(1).ToString('yyyy-MM-dd'); description = 'Fase 2 split'
  externalReference = [guid]::NewGuid().ToString()
  split = @(@{ walletId = $sub.walletId; percentualValue = 100 })
}
$idCobranca = $cobranca.Corpo.id
$null = Chamar 'pagar-no-simulador' 'POST' "sandbox/payment/$idCobranca/confirm" @{} -EsperaErro
Start-Sleep -Seconds 3
$detalhe = Chamar 'cobranca-detalhe' 'GET' "payments/$idCobranca"
$evidencias.respostas['statusAposPagamento'] = $detalhe.Corpo.status
$evidencias.respostas['splitNaCobranca'] = $detalhe.Corpo.split

# 5a. Lado da subconta: o valor do split ficou disponível ou bloqueado? Há uma cobrança própria com garantia?
$evidencias.respostas['escrowNaCobrancaDaRaiz'] = $detalhe.Corpo.escrow
$saldoSub = Chamar 'saldo-subconta' 'GET' 'finance/balance' -chave $chaveSub -EsperaErro
$evidencias.respostas['saldoSubconta'] = $saldoSub.Corpo
$null = Chamar 'extrato-subconta' 'GET' 'financialTransactions?limit=10' -chave $chaveSub -EsperaErro
$cobrancasSub = Chamar 'cobrancas-vistas-pela-subconta' 'GET' 'payments?limit=10' -chave $chaveSub -EsperaErro
$evidencias.respostas['cobrancasNaSubconta'] = @($cobrancasSub.Corpo.data | ForEach-Object { [ordered]@{ id = $_.id; value = $_.value; status = $_.status; escrow = $_.escrow } })
foreach ($c in @($cobrancasSub.Corpo.data)) {
  $null = Chamar "garantia-da-cobranca-da-subconta-$($c.id)" 'GET' "payments/$($c.id)/escrow" -chave $chaveSub -EsperaErro
}

# 5. Q1/Q2: a garantia da cobrança, com cada chave.
$garantiaSub = Chamar 'Q2-garantia-chave-subconta' 'GET' "payments/$idCobranca/escrow" -chave $chaveSub -EsperaErro
$garantiaRaiz = Chamar 'Q2-garantia-chave-raiz' 'GET' "payments/$idCobranca/escrow" -EsperaErro
$evidencias.respostas['Q2_garantiaComChaveSubconta'] = $garantiaSub.Status
$evidencias.respostas['Q2_garantiaComChaveRaiz'] = $garantiaRaiz.Status
$garantia = if ($garantiaSub.Status -lt 400 -and $garantiaSub.Corpo.id) { $garantiaSub.Corpo } elseif ($garantiaRaiz.Status -lt 400 -and $garantiaRaiz.Corpo.id) { $garantiaRaiz.Corpo } else { $null }
$evidencias.respostas['Q1_valorFicouRetido'] = [bool]$garantia
$evidencias.respostas['Q2_statusDaGarantia'] = $garantia.status

# 6. Q1: encerrar a garantia (libera o valor à subconta).
if ($garantia) {
  $fimSub = Chamar 'Q1-finish-chave-subconta' 'POST' "escrow/$($garantia.id)/finish" $null -chave $chaveSub -EsperaErro
  $evidencias.respostas['Q1_finishComChaveSubconta'] = $fimSub.Status
  if ($fimSub.Status -ge 400) {
    $fimRaiz = Chamar 'Q1-finish-chave-raiz' 'POST' "escrow/$($garantia.id)/finish" -EsperaErro
    $evidencias.respostas['Q1_finishComChaveRaiz'] = $fimRaiz.Status
  }
  Start-Sleep -Seconds 2
  $depois = Chamar 'garantia-depois-do-finish' 'GET' "payments/$idCobranca/escrow" -chave $chaveSub -EsperaErro
  $evidencias.respostas['garantiaDepoisDoFinish'] = $depois.Corpo
}

# 7. Hipótese alternativa: o escrow vale para cobranças da PRÓPRIA subconta (criadas com a chave dela)?
$clienteSub = Chamar 'cliente-da-subconta' 'POST' 'customers' @{
  name = 'Pagador Fase 2 Sub'; email = 'fase2-sub@exemplo.com'; cpfCnpj = '12345678909'; notificationDisabled = $true
} -chave $chaveSub -EsperaErro
if ($clienteSub.Status -lt 400) {
  $cobrancaSub = Chamar 'cobranca-da-subconta' 'POST' 'payments' @{
    customer = $clienteSub.Corpo.id; billingType = 'PIX'; value = 10.00
    dueDate = (Get-Date).AddDays(1).ToString('yyyy-MM-dd'); description = 'Fase 2 cobranca propria'
    externalReference = [guid]::NewGuid().ToString()
  } -chave $chaveSub -EsperaErro
  if ($cobrancaSub.Status -lt 400) {
    $idSub = $cobrancaSub.Corpo.id
    $null = Chamar 'pagar-cobranca-da-subconta' 'POST' "sandbox/payment/$idSub/confirm" @{} -chave $chaveSub -EsperaErro
    Start-Sleep -Seconds 3
    $detSub = Chamar 'cobranca-da-subconta-detalhe' 'GET' "payments/$idSub" -chave $chaveSub -EsperaErro
    $gSub = Chamar 'garantia-da-cobranca-propria' 'GET' "payments/$idSub/escrow" -chave $chaveSub -EsperaErro
    $evidencias.respostas['cobrancaPropria_status'] = $detSub.Corpo.status
    $evidencias.respostas['cobrancaPropria_escrowNoDetalhe'] = $detSub.Corpo.escrow
    $evidencias.respostas['cobrancaPropria_garantia'] = [ordered]@{ http = $gSub.Status; corpo = $gSub.Corpo }
    $saldoAntes = Chamar 'saldo-subconta-com-garantia' 'GET' 'finance/balance' -chave $chaveSub -EsperaErro
    $evidencias.respostas['saldoSubcontaComGarantiaAtiva'] = $saldoAntes.Corpo
    if ($gSub.Status -lt 400 -and $gSub.Corpo.id) {
      # Q1: encerrar a garantia da cobrança própria, primeiro com a chave da subconta, depois com a da raiz.
      $fim = Chamar 'Q1-finish-cobranca-propria-chave-subconta' 'POST' "escrow/$($gSub.Corpo.id)/finish" $null -chave $chaveSub -EsperaErro
      $evidencias.respostas['Q1_finishChaveSubconta'] = [ordered]@{ http = $fim.Status; corpo = $fim.Corpo }
      if ($fim.Status -ge 400) {
        $fimRaiz = Chamar 'Q1-finish-cobranca-propria-chave-raiz' 'POST' "escrow/$($gSub.Corpo.id)/finish" -EsperaErro
        $evidencias.respostas['Q1_finishChaveRaiz'] = [ordered]@{ http = $fimRaiz.Status; corpo = $fimRaiz.Corpo }
      }
      Start-Sleep -Seconds 3
      $gDepois = Chamar 'garantia-apos-finish' 'GET' "payments/$idSub/escrow" -chave $chaveSub -EsperaErro
      $evidencias.respostas['garantiaAposFinish'] = $gDepois.Corpo
      $repetido = Chamar 'Q1-finish-repetido' 'POST' "escrow/$($gSub.Corpo.id)/finish" $null -chave $chaveSub -EsperaErro
      $evidencias.respostas['Q1_finishRepetido'] = [ordered]@{ http = $repetido.Status; corpo = $repetido.Corpo }
    }
    $saldoFinal = Chamar 'saldo-subconta-final' 'GET' 'finance/balance' -chave $chaveSub -EsperaErro
    $evidencias.respostas['saldoSubcontaFinal'] = $saldoFinal.Corpo
  }
}

Gravar
Write-Host "Evidências gravadas em $Saida (chaves mascaradas)."
$evidencias.respostas | ConvertTo-Json -Depth 10 | ForEach-Object { Mascarar $_ }
