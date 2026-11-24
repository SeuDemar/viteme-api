#requires -Version 7
<#
.SYNOPSIS
  Fase 0 do plano Asaas (.claude/planning/pagamentos-asaas.md): prova no sandbox, sem código de produção.

.DESCRIPTION
  Roda o fluxo cliente -> cobrança Pix -> QR Code -> pagamento simulado -> estorno e grava as respostas reais em
  docs/evidencias/asaas-fase0-AAAA-MM-DD.json, com a chave de API e qualquer apiKey de subconta mascaradas.
  Responde as perguntas Q4 (CPF do pagador) e Q6 (parcialmente). Com -ComSubconta, também cria uma subconta de teste
  (Q3) e uma cobrança com split para ela (Q1/Q2 pedem conferência no painel: veja o README desta pasta).

  Só roda contra o sandbox: aborta se a URL não contiver "sandbox".

  A chave vem SÓ da variável de ambiente ASAAS_API_KEY (chave do sandbox: Integrações > Chaves de API, no painel
  https://sandbox.asaas.com). Nunca passe a chave por parâmetro nem a grave em arquivo.

.EXAMPLE
  $env:ASAAS_API_KEY = '<chave do sandbox>'
  ./docs/asaas/fase0-sandbox.ps1
  ./docs/asaas/fase0-sandbox.ps1 -ComSubconta
#>
[CmdletBinding()]
param(
  [string] $UrlBase = 'https://api-sandbox.asaas.com/v3',
  [switch] $ComSubconta,
  [string] $Saida
)

$ErrorActionPreference = 'Stop'

if ($UrlBase -notmatch 'sandbox') { throw 'Este script só roda contra o sandbox do Asaas.' }
$chave = $env:ASAAS_API_KEY
if ([string]::IsNullOrWhiteSpace($chave)) { throw 'Defina ASAAS_API_KEY (chave do sandbox) antes de rodar.' }

$hoje = Get-Date -Format 'yyyy-MM-dd'
if (-not $Saida) { $Saida = Join-Path $PSScriptRoot "..\evidencias\asaas-fase0-$hoje.json" }

$cabecalhos = @{ 'access_token' = $chave; 'User-Agent' = 'Viteme-Fase0/1.0'; 'Content-Type' = 'application/json' }
$evidencias = [ordered]@{ executadoEm = (Get-Date).ToString('o'); urlBase = $UrlBase; passos = @() }

function Mascarar([string] $texto) {
  $texto = $texto.Replace($chave, '***')
  # Subcontas devolvem a chave da própria subconta: nunca vai para o repositório.
  return [regex]::Replace($texto, '"apiKey"\s*:\s*"[^"]*"', '"apiKey":"***"')
}

function Chamar([string] $nome, [string] $metodo, [string] $rota, $corpo = $null, [switch] $EsperaErro) {
  $params = @{ Uri = "$UrlBase/$rota"; Method = $metodo; Headers = $cabecalhos; SkipHttpErrorCheck = $true; StatusCodeVariable = 'codigo' }
  if ($null -ne $corpo) { $params.Body = ($corpo | ConvertTo-Json -Depth 10 -Compress) }
  $resposta = Invoke-RestMethod @params
  $passo = [ordered]@{ passo = $nome; requisicao = "$metodo /$rota"; status = $codigo; resposta = $resposta }
  $script:evidencias.passos += $passo
  Write-Host ("[{0}] {1} -> HTTP {2}" -f $nome, "$metodo /$rota", $codigo)
  if ($codigo -ge 400 -and -not $EsperaErro) {
    Gravar
    throw "Passo '$nome' falhou (HTTP $codigo). Evidências parciais gravadas em $Saida."
  }
  return $resposta
}

function Gravar {
  $json = $evidencias | ConvertTo-Json -Depth 20
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Saida) | Out-Null
  Set-Content -Path $Saida -Value (Mascarar $json) -Encoding UTF8
}

# 0. A chave funciona e a conta é a do sandbox.
$null = Chamar 'saldo' 'GET' 'finance/balance'

# Q4: criar cliente SEM cpfCnpj. Esperado: erro (documentação diz que name e cpfCnpj são obrigatórios).
$semCpf = Chamar 'Q4-cliente-sem-cpf' 'POST' 'customers' @{ name = 'Pagador Sem CPF'; email = 'sem-cpf@exemplo.com' } -EsperaErro
$evidencias['Q4_clienteSemCpfAceito'] = ($script:evidencias.passos[-1].status -lt 400)

# Q4b: o cliente sem CPF aceita uma cobrança Pix? (o cliente foi aceito, mas a cobrança pode exigir o documento)
if ($evidencias['Q4_clienteSemCpfAceito']) {
  $null = Chamar 'Q4b-cobranca-cliente-sem-cpf' 'POST' 'payments' @{
    customer = $semCpf.id; billingType = 'PIX'; value = 10.00
    dueDate = (Get-Date).AddDays(1).ToString('yyyy-MM-dd'); description = 'Fase 0 sem CPF'
    externalReference = [guid]::NewGuid().ToString()
  } -EsperaErro
  $evidencias['Q4b_cobrancaSemCpfAceita'] = ($script:evidencias.passos[-1].status -lt 400)
}

# Cliente com CPF de teste válido (dígitos verificadores corretos), com externalReference como o adaptador faz.
$cliente = Chamar 'cliente' 'POST' 'customers' @{
  name = 'Pagador Fase 0'; email = 'fase0@exemplo.com'; cpfCnpj = '12345678909'
  externalReference = "fase0-$hoje"; notificationDisabled = $true
}

# Cobrança Pix, com a referência externa e o vencimento de amanhã.
$cobranca = Chamar 'cobranca-pix' 'POST' 'payments' @{
  customer = $cliente.id; billingType = 'PIX'; value = 10.00
  dueDate = (Get-Date).AddDays(1).ToString('yyyy-MM-dd'); description = 'Fase 0 Viteme'
  externalReference = [guid]::NewGuid().ToString()
}
$evidencias['cobrancaId'] = $cobranca.id
$evidencias['invoiceUrl'] = $cobranca.invoiceUrl

# Idempotência por externalReference (usada pelo adaptador antes de criar).
$null = Chamar 'busca-por-referencia' 'GET' "payments?externalReference=$($cobranca.externalReference)"

# QR Code Pix: o frontend deve mostrar "copia e cola" (payload) e a imagem.
# No sandbox a conta precisa ter uma chave Pix cadastrada (painel: Pix > Minhas chaves), senão responde 400.
$qr = Chamar 'pix-qrcode' 'GET' "payments/$($cobranca.id)/pixQrCode" -EsperaErro
$evidencias['Q_pixQrCodeObtido'] = ($script:evidencias.passos[-1].status -lt 400)
if (-not $evidencias['Q_pixQrCodeObtido']) { Write-Warning 'Sem QR Code: cadastre uma chave Pix na conta do sandbox. Seguindo com o resto do fluxo.' }

# Pagamento simulado (sandbox) e conferência do status.
$null = Chamar 'confirmar-pagamento-sandbox' 'POST' "sandbox/payment/$($cobranca.id)/confirm" @{}
Start-Sleep -Seconds 2
$pago = Chamar 'status-apos-pagamento' 'GET' "payments/$($cobranca.id)"
$evidencias['statusApoPagamento'] = $pago.status

# Estorno total e conferência (o adaptador consulta antes de estornar: P30).
$null = Chamar 'estorno' 'POST' "payments/$($cobranca.id)/refund" @{ value = 10.00; description = 'Fase 0' } -EsperaErro
Start-Sleep -Seconds 2
$estornado = Chamar 'status-apos-estorno' 'GET' "payments/$($cobranca.id)"
$evidencias['statusApoEstorno'] = $estornado.status

if ($ComSubconta) {
  # Q3: campos exigidos da subconta PJ (a resposta de erro, se faltar algo, lista os campos). Dados fictícios de teste.
  $subconta = Chamar 'Q3-subconta' 'POST' 'accounts' @{
    name = 'Organizador Fase 0'; email = "organizador-fase0-$hoje@exemplo.com"; cpfCnpj = '34238864000168'
    companyType = 'LIMITED'; mobilePhone = '11999999999'; incomeValue = 5000
    address = 'Rua de Teste'; addressNumber = '100'; province = 'Centro'; postalCode = '01310100'
  } -EsperaErro
  $ok = $script:evidencias.passos[-1].status -lt 400
  $evidencias['Q3_subcontaCriada'] = $ok
  if ($ok) {
    $evidencias['subcontaWalletId'] = $subconta.walletId
    # Cobrança com split de 100% para a subconta (modelo do plano). Q1/Q2: conferir no painel se o valor ficou em garantia.
    $cobrancaSplit = Chamar 'cobranca-com-split' 'POST' 'payments' @{
      customer = $cliente.id; billingType = 'PIX'; value = 10.00
      dueDate = (Get-Date).AddDays(1).ToString('yyyy-MM-dd'); description = 'Fase 0 split'
      externalReference = [guid]::NewGuid().ToString()
      split = @(@{ walletId = $subconta.walletId; percentualValue = 100 })
    } -EsperaErro
    if ($script:evidencias.passos[-1].status -lt 400) {
      $null = Chamar 'pagamento-com-split' 'POST' "sandbox/payment/$($cobrancaSplit.id)/confirm" @{} -EsperaErro
      Start-Sleep -Seconds 2
      $null = Chamar 'cobranca-split-detalhe' 'GET' "payments/$($cobrancaSplit.id)"
    }
  }
}

Gravar
Write-Host "Evidências gravadas em $Saida (chaves mascaradas). Responda Q1-Q6 em .claude/planning/pagamentos-asaas.md."
