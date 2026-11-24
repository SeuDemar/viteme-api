#requires -Version 7
<#
.SYNOPSIS
  Cadastra (ou atualiza) no Asaas o webhook de cobranças que aponta para a API do Viteme.

.DESCRIPTION
  Só roda contra o sandbox. A chave vem de ASAAS_API_KEY e o token do webhook de ASAAS_WEBHOOK_TOKEN: o mesmo valor de
  Gateway__Asaas__TokenWebhook na API (o Asaas o devolve no cabeçalho asaas-access-token de cada entrega).
  A API publicada precisa estar com Gateway__Provedor=Asaas para validar o token; senão responde 401 e o Asaas repete e,
  depois de 15 falhas seguidas, pausa a fila.

.EXAMPLE
  $env:ASAAS_API_KEY = '<chave do sandbox>'
  $env:ASAAS_WEBHOOK_TOKEN = '<token, o mesmo da API>'
  ./docs/asaas/registrar-webhook.ps1 -UrlApi https://minha-api.azurewebsites.net -Email voce@exemplo.com
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $UrlApi,
  [Parameter(Mandatory)] [string] $Email,
  [string] $UrlBase = 'https://api-sandbox.asaas.com/v3'
)

$ErrorActionPreference = 'Stop'
if ($UrlBase -notmatch 'sandbox') { throw 'Este script só roda contra o sandbox do Asaas.' }
if ([string]::IsNullOrWhiteSpace($env:ASAAS_API_KEY)) { throw 'Defina ASAAS_API_KEY (chave do sandbox).' }
if ([string]::IsNullOrWhiteSpace($env:ASAAS_WEBHOOK_TOKEN)) { throw 'Defina ASAAS_WEBHOOK_TOKEN (o mesmo valor de Gateway__Asaas__TokenWebhook).' }

$cabecalhos = @{ 'access_token' = $env:ASAAS_API_KEY; 'User-Agent' = 'Viteme-Fase0/1.0'; 'Content-Type' = 'application/json' }
$destino = $UrlApi.TrimEnd('/') + '/api/v1/pagamentos/webhook'

$corpo = @{
  name        = 'Viteme API'
  url         = $destino
  email       = $Email
  enabled     = $true
  interrupted = $false
  authToken   = $env:ASAAS_WEBHOOK_TOKEN
  sendType    = 'SEQUENTIALLY'
  # Os eventos que mudam o pagamento no Viteme; os demais são ignorados pelo adaptador (200 sem efeito).
  events      = @('PAYMENT_RECEIVED', 'PAYMENT_CONFIRMED', 'PAYMENT_OVERDUE', 'PAYMENT_DELETED', 'PAYMENT_REFUNDED')
} | ConvertTo-Json -Depth 5 -Compress

$resposta = Invoke-RestMethod -Uri "$UrlBase/webhooks" -Method Post -Headers $cabecalhos -Body $corpo -SkipHttpErrorCheck -StatusCodeVariable codigo
Write-Host "POST /webhooks -> HTTP $codigo"
$resposta | ConvertTo-Json -Depth 5 | ForEach-Object { $_.Replace($env:ASAAS_WEBHOOK_TOKEN, '***') }
if ($codigo -ge 400) { throw "O Asaas recusou o cadastro do webhook (HTTP $codigo)." }
Write-Host "Webhook cadastrado para $destino. Faça um pagamento simulado (sandbox/payment/{id}/confirm) e confira o status na API."
