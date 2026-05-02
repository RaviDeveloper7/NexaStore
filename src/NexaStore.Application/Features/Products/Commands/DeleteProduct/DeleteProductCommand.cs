using MediatR;

namespace NexaStore.Application.Features.Products.Commands.DeleteProduct;

public class DeleteProductCommand : IRequest<Unit>
{
    public Guid Id { get; set; }

    // Constructor for convenient instantiation from the controller
    public DeleteProductCommand(Guid id) => Id = id;
}
