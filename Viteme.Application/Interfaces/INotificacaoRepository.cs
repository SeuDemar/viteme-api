using Viteme.Domain.Entities;

namespace Viteme.Application.Interfaces;

public interface INotificacaoRepository
{
    void Adicionar(Notificacao notificacao);
}
