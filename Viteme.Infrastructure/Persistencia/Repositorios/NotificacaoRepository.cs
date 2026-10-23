using Viteme.Application.Interfaces;
using Viteme.Domain.Entities;

namespace Viteme.Infrastructure.Persistencia.Repositorios;

public class NotificacaoRepository(VitemeDbContext contexto) : INotificacaoRepository
{
    public void Adicionar(Notificacao notificacao) => contexto.Notificacoes.Add(notificacao);
}
